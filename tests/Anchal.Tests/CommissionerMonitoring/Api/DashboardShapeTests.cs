using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Anchal.Application.CommissionerMonitoring.Auth;
using Anchal.Application.CommissionerMonitoring.DTOs;
using Anchal.Application.CommissionerMonitoring.Services;
using Anchal.Domain.CommissionerMonitoring;
using Anchal.Domain.CommissionerMonitoring.Rules;
using Anchal.Infrastructure.CommissionerMonitoring.Persistence;

namespace Anchal.Tests.CommissionerMonitoring.Api;

// The dashboard counts each figure only over the shapes that have it. Every fixture here carries a revenue
// service with payments and field-work services with work done ALONGSIDE counter entries, so each assertion
// fails against the old all-25-services sums.
public sealed class DashboardShapeTests
{
    private static readonly DateOnly Day1 = new(2026, 10, 1);
    private static readonly DateOnly Day2 = new(2026, 10, 2);

    // Break caught: pending summed closing.Service across all 25 services, so an opening balance on a revenue or
    // field-work service (79 here) swamped the counter figure (4 + 10 - 5 = 9).
    [Fact]
    public async Task PendingCountsOnlyTheCounterShape()
    {
        await using var f = await Fixture.Create();
        var row = await f.Row(Day1, Day1);
        Assert.Equal(9, row.Pending);
    }

    // Break caught: field work contributes completed against received of 0, so completed/received was 15/13 = 115% with all services summed.
    [Fact]
    public async Task DisposalPercentageNeverExceedsOneHundred()
    {
        await using var f = await Fixture.Create();
        var row = await f.Row(Day1, Day1);
        Assert.Equal(50, row.DisposalPct);
        Assert.True(row.DisposalPct <= 100);
        Assert.Equal(5, row.Completed);
        Assert.Equal(10, row.Received);
    }

    // Break caught: WorkDone must sum the eight field-work services in the filtered range, not counter or revenue
    // completed, and not field work filed on a day outside the range (100 on Day2).
    [Fact]
    public async Task WorkDoneSumsTheFieldWorkServicesOnly()
    {
        await using var f = await Fixture.Create();
        Assert.Equal(10, (await f.Row(Day1, Day1)).WorkDone);
        Assert.Equal(110, (await f.Row(Day1, Day2)).WorkDone);
    }

    // Break caught: nothing feeds complaints, field tasks or SLA any more, yet legacy values on a counter entry
    // (9 complaints pending, 20 field tasks pending, 0% SLA) kept firing their reasons.
    [Fact]
    public async Task RetiredReviewReasonsNoLongerFire()
    {
        await using var f = await Fixture.Create(counterOpening: 0, counterReceived: 5, counterCompleted: 5,
            legacyComplaints: 9, legacyField: 20);
        var row = await f.Row(Day1, Day1);
        Assert.Equal(0, row.SlaPct);
        Assert.Equal(9, row.ComplaintsPending);
        Assert.Equal(20, row.FieldPending);
        Assert.DoesNotContain("Complaints pending > 5", row.ReviewReasons);
        Assert.DoesNotContain("Field tasks pending > 10", row.ReviewReasons);
        Assert.DoesNotContain("SLA < 90%", row.ReviewReasons);
        Assert.Empty(row.ReviewReasons);
    }

    // Break caught: sla summed all 25 services while slaPct divides by counter-only completed, so a pre-cutover revenue
    // or field-work entry carrying slaWithinTime (40 + 40 here) pushed the percentage to 85/5 = 1700% and into the sla figure.
    [Fact]
    public async Task SlaCountsOnlyTheCounterShapeAndNeverExceedsOneHundred()
    {
        await using var f = await Fixture.Create(counterSla: 5, legacySla: 40);
        var row = await f.Row(Day1, Day1);
        Assert.Equal(5, row.SlaWithinTime);
        Assert.Equal(100, row.SlaPct);
        Assert.True(row.SlaPct <= 100);
    }

    // Break caught: the surviving reasons must keep firing, on counter pending only, with their English text unchanged
    // (i18n.tsx matches them by exact key).
    [Fact]
    public async Task SurvivingReasonsKeepTheirTextAndScopeToCounter()
    {
        await using var f = await Fixture.Create(counterOpening: 20, counterReceived: 10, counterCompleted: 5);
        var row = await f.Row(Day1, Day1);
        Assert.Equal(25, row.Pending);
        Assert.Equal(["Pending > 10", "Disposal < 90%"], row.ReviewReasons);

        await using var quiet = await Fixture.Create(counterOpening: 0, counterReceived: 5, counterCompleted: 5);
        // Revenue and field-work opening balances (50 and 20) must not trip "Pending > 10" on their own.
        Assert.Empty((await quiet.Row(Day1, Day1)).ReviewReasons);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private int requests;
        private CommissionerMonitoringDbContext Db { get; } = new(new DbContextOptionsBuilder<CommissionerMonitoringDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        private readonly Guid office = Guid.NewGuid(), user = Guid.NewGuid(), commissioner = Guid.NewGuid();
        private MonitoringService Service => new(Db, new Clock());

        public async Task<MonitoringRowDto> Row(DateOnly from, DateOnly to) =>
            Assert.Single((await Service.GetDashboardAsync(Principal(commissioner, null), new PeriodFilter(from, to)))
                .Value!.Data.Rows);

        private static ClaimsPrincipal Principal(Guid id, Guid? officeId)
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, id.ToString()),
                new(MonitoringPolicies.UserIdClaim, id.ToString()),
                new(MonitoringPolicies.ScopeClaim, MonitoringPolicies.ScopeValue),
                new(MonitoringPolicies.RoleClaim, officeId.HasValue ? MonitoringPolicies.OfficeRole : MonitoringPolicies.CommissionerRole) };
            if (officeId.HasValue) claims.Add(new(MonitoringPolicies.OfficeIdClaim, officeId.ToString()!));
            return new(new ClaimsIdentity(claims, MonitoringPolicies.AuthenticationScheme));
        }

        private async Task Save(EntryInput input)
        {
            var result = await Service.SaveEntryAsync(Principal(user, office),
                new($"entry-request-{++requests}", null, "backdate", input, "Late"));
            Assert.True(result.IsSuccess, result.Error?.Message);
        }

        public static async Task<Fixture> Create(long counterOpening = 4, long counterReceived = 10, long counterCompleted = 5,
            long legacyComplaints = 0, long legacyField = 0, long counterSla = 0, long legacySla = 0)
        {
            var f = new Fixture();
            f.Db.Offices.Add(new MonitoringOffice { Id = f.office, Code = "anchal1", Name = "Anchal 1" });
            f.Db.Users.AddRange(new MonitoringUser { Id = f.user, LoginName = "anchal1", Role = MonitoringPolicies.OfficeRole },
                new MonitoringUser { Id = f.commissioner, LoginName = "commissioner", Role = MonitoringPolicies.CommissionerRole });
            f.Db.UserOffices.Add(new MonitoringUserOffice { UserId = f.user, OfficeId = f.office });
            f.Db.CatalogItems.AddRange(Enumerable.Range(1, 25).Select(x => new MonitoringCatalogItem {
                Id = Guid.NewGuid(), Key = x.ToString(), Name = $"Service {x}", Kind = MonitoringActivityKind.Service, SortOrder = x }));
            await f.Db.SaveChangesAsync();

            // Service 1 is counter, 2 is revenue, 8 and 9 are field work. The non-counter openings are
            // nonzero on purpose: the old sums counted them.
            var openings = Enumerable.Range(1, 25).Select(x => new OpeningBalanceDto(x,
                x == 1 ? new(counterOpening, 0, 0) : x == 2 ? new(50, 0, 0) : x == 8 ? new(20, 0, 0) : new(0, 0, 0))).ToArray();
            var setup = await f.Service.SaveSetupAsync(Principal(f.user, f.office), new("setup-request", null, Day1, openings));
            Assert.True(setup.IsSuccess, setup.Error?.Message);

            await f.Save(new(1, Day1, "Ward 1", counterReceived, counterCompleted, counterSla, 0, legacyComplaints, 0, legacyField, 0, null, ""));
            await f.Save(new(2, Day1, "Ward 1", 3, 0, legacySla, 900, 0, 0, 0, 0, null, ""));
            await f.Save(new(8, Day1, "Ward 1", 0, 7, legacySla, 0, 0, 0, 0, 0, null, ""));
            await f.Save(new(9, Day1, "Ward 1", 0, 3, 0, 0, 0, 0, 0, 0, null, ""));
            await f.Save(new(8, Day2, "Ward 1", 0, 100, 0, 0, 0, 0, 0, 0, null, ""));
            return f;
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.Parse("2026-10-05T18:30:00Z");
    }
}
