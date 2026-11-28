using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Anchal.Application.CommissionerMonitoring.Auth;
using Anchal.Application.CommissionerMonitoring.DTOs;
using Anchal.Application.CommissionerMonitoring.Services;
using Anchal.Domain.CommissionerMonitoring;
using Anchal.Domain.CommissionerMonitoring.Rules;
using Anchal.Infrastructure.CommissionerMonitoring.Persistence;

namespace Anchal.Tests.CommissionerMonitoring.Application;

/// The server, not the form, refuses field work on the daily entry; the denominator follows the same rule;
/// and a field-work entry filed before the cutover stays readable and is read-only.
public sealed class DailyEntryFieldWorkTests
{
    private static readonly DateOnly Day1 = new(2026, 10, 1);
    private static readonly DateOnly Day3 = new(2026, 10, 3);

    private static EntryInput Input(int service, DateOnly date, long received = 0, long completed = 0) =>
        new(service, date, "Ward 1", received, completed, 0, 0, 0, 0, 0, 0, null, "");

    // ---- refusal ----

    [Theory]
    [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(13)]
    [InlineData(14)] [InlineData(15)] [InlineData(16)] [InlineData(17)]
    public async Task APostedFieldWorkEntryIsRefusedAndNothingIsStored(int service)
    {
        await using var f = await Fixture.Create();
        var result = await f.Save(Input(service, Day1, completed: 5));

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.Status);
        Assert.True(result.Error!.FieldErrors!.ContainsKey("serviceId"));
        Assert.Empty(await f.Db.AuditEvents.Where(x => x.AggregateType == "entry").ToListAsync());
        Assert.Empty(await f.Db.Reports.ToListAsync());
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(11)] [InlineData(20)]
    public async Task ACollectedServiceIsStillAccepted(int service)
    {
        await using var f = await Fixture.Create();
        var result = await f.Save(Input(service, Day1, received: service == 2 ? 1 : 5, completed: service == 2 ? 0 : 2));

        Assert.True(result.IsSuccess, result.Error?.Message);
    }

    // ---- a field-work entry filed before the cutover ----

    [Fact]
    public async Task APreCutoverFieldWorkEntryIsReadableAndStillCountedInItsFigure()
    {
        await using var f = await Fixture.Create();
        var old = f.SeedPreCutover(Input(9, Day1, completed: 7));
        await f.Db.SaveChangesAsync();

        var read = await f.Service.GetEntriesAsync(f.Office, "backdate", Day1);

        Assert.True(read.IsSuccess, read.Error?.Message);
        var entry = Assert.Single(read.Value!.Data.Entries, x => x.Input.ServiceId == 9);
        Assert.Equal(old, entry.Id);
        Assert.Equal(7, entry.Input.Completed);
    }

    [Fact]
    public async Task ACorrectionToAPreCutoverFieldWorkEntryIsRefusedNotThrown()
    {
        await using var f = await Fixture.Create();
        var id = f.SeedPreCutover(Input(9, Day1, completed: 7));
        await f.Db.SaveChangesAsync();

        var result = await f.Service.SaveEntryAsync(f.Office, new("correct-field-1", 1, "backdate",
            Input(9, Day1, completed: 8), "Recount"), id);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.Status);
        Assert.True(result.Error!.FieldErrors!.ContainsKey("serviceId"));
        // Read-only means unchanged: still version 1, still 7, and no correction row was written.
        var after = Assert.Single((await f.Service.GetEntriesAsync(f.Office, "backdate", Day1)).Value!.Data.Entries,
            x => x.Input.ServiceId == 9);
        Assert.Equal(1, after.Version);
        Assert.Equal(7, after.Input.Completed);
        Assert.Empty(await f.Db.Corrections.ToListAsync());
    }

    [Fact]
    public async Task ACounterEntryOnTheSameDayStillCorrectsAlongsideAnOldFieldWorkOne()
    {
        await using var f = await Fixture.Create();
        f.SeedPreCutover(Input(9, Day1, completed: 7));
        await f.Db.SaveChangesAsync();
        var filed = (await f.Save(Input(11, Day1, received: 5, completed: 2))).Value!;

        var fixedUp = await f.Service.SaveEntryAsync(f.Office, new("correct-counter-1", filed.Version, "backdate",
            Input(11, Day1, received: 6, completed: 2), "Recount"), filed.Id);

        Assert.True(fixedUp.IsSuccess, fixedUp.Error?.Message);
    }

    [Fact]
    public async Task ADashboardOverAPreCutoverFieldWorkEntryDoesNotThrow()
    {
        await using var f = await Fixture.Create();
        f.SeedPreCutover(Input(9, Day1, completed: 7));
        await f.Db.SaveChangesAsync();

        var all = await f.Service.GetDashboardAsync(f.Commissioner, new PeriodFilter(Day1, Day3));
        var pinned = await f.Service.GetDashboardAsync(f.Commissioner, new PeriodFilter(Day1, Day3, null, 9));

        Assert.True(all.IsSuccess, all.Error?.Message);
        Assert.True(pinned.IsSuccess, pinned.Error?.Message);
    }

    // ---- the denominator ----

    [Fact]
    public async Task UnpinnedExpectedIsSeventeenReportsPerDayAndPinnedIsOne()
    {
        await using var f = await Fixture.Create();

        var all = (await f.Service.GetDashboardAsync(f.Commissioner, new PeriodFilter(Day1, Day3))).Value!.Data.Rows.Single();
        var pinned = (await f.Service.GetDashboardAsync(f.Commissioner, new PeriodFilter(Day1, Day3, null, 1))).Value!.Data.Rows.Single();

        Assert.Equal(3 * 17, all.Expected);
        Assert.Equal(3, pinned.Expected);
    }

    [Fact]
    public async Task UnpinnedExpectedIsTheCountOfWhatTheDailyEntryCollects()
    {
        await using var f = await Fixture.Create();

        var row = (await f.Service.GetDashboardAsync(f.Commissioner, new PeriodFilter(Day1, Day3))).Value!.Data.Rows.Single();

        Assert.Equal(3 * MonitoringRules.DailyEntryServiceCount, row.Expected);
    }

    /// The denominator must come from the rule. A literal here, 25 or 17 alike, is what drifted before.
    [Fact]
    public void TheDenominatorExpressionCarriesNoNumericLiteralBesideThePinnedOne()
    {
        var path = Path.Combine(Domain.DailyEntryCollectsTests.SourceRoot(), "Anchal.Application",
            "CommissionerMonitoring", "Services", "MonitoringService.cs");
        var line = Assert.Single(File.ReadAllLines(path), l => l.Contains("filter.ServiceId.HasValue ? 1 :"));
        var unpinned = line[(line.IndexOf("? 1 :", StringComparison.Ordinal) + "? 1 :".Length)..];

        Assert.DoesNotMatch(@"\d", unpinned);
        Assert.Contains("DailyEntryServiceCount", unpinned);
    }

    /// Submitted is compared with Expected. A pre-cutover field-work entry is in neither the 17 nor the
    /// count, or an office could read as 20 of 17 and look fully filed.
    [Fact]
    public async Task APreCutoverFieldWorkEntryDoesNotInflateTheFiledCount()
    {
        await using var f = await Fixture.Create();
        f.SeedPreCutover(Input(9, Day1, completed: 7));
        f.SeedPreCutover(Input(8, Day1, completed: 3));
        await f.Db.SaveChangesAsync();
        Assert.True((await f.Save(Input(11, Day1, received: 5, completed: 2))).IsSuccess);

        var row = (await f.Service.GetDashboardAsync(f.Commissioner, new PeriodFilter(Day1, Day1))).Value!.Data.Rows.Single();

        Assert.Equal(1, row.Submitted);
    }

    /// Pinned to one field-work service, the filed count is that service's own history, not zero of N.
    [Fact]
    public async Task PinnedToAFieldWorkServiceTheHistoricEntryStillCountsAsFiled()
    {
        await using var f = await Fixture.Create();
        f.SeedPreCutover(Input(9, Day1, completed: 7));
        await f.Db.SaveChangesAsync();

        var row = (await f.Service.GetDashboardAsync(f.Commissioner, new PeriodFilter(Day1, Day3, null, 9))).Value!.Data.Rows.Single();

        Assert.Equal(1, row.Submitted);
        Assert.Equal(3, row.Expected);
    }

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.Parse("2026-10-05T18:30:00Z");
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public CommissionerMonitoringDbContext Db { get; } = new(new DbContextOptionsBuilder<CommissionerMonitoringDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        private readonly Guid office = Guid.NewGuid(), user = Guid.NewGuid(), commissioner = Guid.NewGuid();
        private int requests;
        public MonitoringService Service => new(Db, new Clock());
        public ClaimsPrincipal Office => PrincipalFor(user, office);
        public ClaimsPrincipal Commissioner => PrincipalFor(commissioner, null);

        private static ClaimsPrincipal PrincipalFor(Guid id, Guid? officeId)
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, id.ToString()),
                new(MonitoringPolicies.UserIdClaim, id.ToString()),
                new(MonitoringPolicies.ScopeClaim, MonitoringPolicies.ScopeValue),
                new(MonitoringPolicies.RoleClaim, officeId.HasValue ? MonitoringPolicies.OfficeRole : MonitoringPolicies.CommissionerRole) };
            if (officeId.HasValue) claims.Add(new(MonitoringPolicies.OfficeIdClaim, officeId.ToString()!));
            return new(new ClaimsIdentity(claims, MonitoringPolicies.AuthenticationScheme));
        }

        public Task<MonitoringResult<EntryDto>> Save(EntryInput input) =>
            Service.SaveEntryAsync(Office, new($"entry-request-{++requests}", null, "backdate", input, "Late"));

        public Guid SeedPreCutover(EntryInput input) => PreCutoverEntries.Add(Db, office, user, input, new Clock().GetUtcNow());

        public static async Task<Fixture> Create()
        {
            var f = new Fixture();
            f.Db.Offices.Add(new MonitoringOffice { Id = f.office, Code = "anchal1", Name = "Anchal 1" });
            f.Db.Users.AddRange(new MonitoringUser { Id = f.user, LoginName = "anchal1", Role = MonitoringPolicies.OfficeRole },
                new MonitoringUser { Id = f.commissioner, LoginName = "commissioner", Role = MonitoringPolicies.CommissionerRole });
            f.Db.UserOffices.Add(new MonitoringUserOffice { UserId = f.user, OfficeId = f.office });
            f.Db.CatalogItems.AddRange(Enumerable.Range(1, 25).Select(x => new MonitoringCatalogItem {
                Id = Guid.NewGuid(), Key = x.ToString(), Name = $"Service {x}", Kind = MonitoringActivityKind.Service, SortOrder = x }));
            await f.Db.SaveChangesAsync();
            var setup = await f.Service.SaveSetupAsync(f.Office, new("setup-request", null, Day1,
                Enumerable.Range(1, 25).Select(x => new OpeningBalanceDto(x, new(0, 0, 0))).ToArray()));
            Assert.True(setup.IsSuccess, setup.Error?.Message);
            return f;
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
