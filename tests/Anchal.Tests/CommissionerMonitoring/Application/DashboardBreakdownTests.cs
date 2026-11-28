using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Anchal.Application.CommissionerMonitoring.Auth;
using Anchal.Application.CommissionerMonitoring.DTOs;
using Anchal.Application.CommissionerMonitoring.Services;
using Anchal.Domain.CommissionerMonitoring;
using Anchal.Domain.CommissionerMonitoring.Rules;
using Anchal.Infrastructure.CommissionerMonitoring.Persistence;

namespace Anchal.Tests.CommissionerMonitoring.Application;

/// The dashboard's per-service and per-day breakdowns. They are a second and third roll-up of the entries
/// GetDashboardAsync already rolls up per Anchal, so the tests that matter most hold them against it: a
/// breakdown that does not add up to the Anchal table is a dashboard that contradicts itself.
public sealed class DashboardBreakdownTests
{
    private static readonly DateOnly Day1 = new(2026, 10, 1);
    private static readonly DateOnly Day2 = new(2026, 10, 2);
    private static readonly DateOnly Day3 = new(2026, 10, 3);

    private static EntryInput Input(int service, DateOnly date, long received = 0, long completed = 0, long amountPaise = 0) =>
        new(service, date, "Ward 1", received, completed, 0, amountPaise, 0, 0, 0, 0, null, "");

    /// Two Anchals, three services of three shapes, three days. Service 11 keeps a queue, service 2 takes
    /// payments, service 1 keeps a queue and takes a fee.
    private static async Task<Fixture> Filed(Func<int, long>? opening = null)
    {
        var f = await Fixture.Create(opening);
        await f.Save(1, Input(11, Day1, received: 5, completed: 2));
        await f.Save(1, Input(11, Day2, received: 4, completed: 4));
        await f.Save(2, Input(11, Day1, received: 3, completed: 1));
        await f.Save(1, Input(2, Day1, received: 3, amountPaise: 30000));
        await f.Save(2, Input(1, Day3, received: 2, completed: 1, amountPaise: 50000));
        return f;
    }

    private static async Task<IReadOnlyList<ServiceRowDto>> Services(Fixture f, PeriodFilter filter) =>
        (await f.Service.GetDashboardServicesAsync(f.Commissioner, filter)).Value!.Data.Rows;

    private static async Task<IReadOnlyList<DayRowDto>> Days(Fixture f, PeriodFilter filter) =>
        (await f.Service.GetDashboardDaysAsync(f.Commissioner, filter)).Value!.Data.Rows;

    private static async Task<IReadOnlyList<MonitoringRowDto>> Anchals(Fixture f, PeriodFilter filter) =>
        (await f.Service.GetDashboardAsync(f.Commissioner, filter)).Value!.Data.Rows;

    // ---- by service ----

    [Fact]
    public async Task TheRowsAreExactlyTheServicesTheDailyEntryCollectsInIdOrder()
    {
        await using var f = await Fixture.Create();

        var rows = await Services(f, new PeriodFilter(Day1, Day3));

        Assert.Equal(Enumerable.Range(1, MonitoringRules.ServiceCount).Where(MonitoringRules.DailyEntryCollects),
            rows.Select(x => x.ServiceId));
        Assert.All(rows, row => Assert.Equal((0L, 0L, 0L, 0L), (row.Received, row.Completed, row.Pending, row.AmountPaise)));
    }

    [Fact]
    public async Task AServiceRowIsThatServicesSumAcrossAnchalsForThePeriod()
    {
        await using var f = await Filed();

        var rows = await Services(f, new PeriodFilter(Day1, Day2));
        var queue = rows.Single(x => x.ServiceId == 11);
        var payments = rows.Single(x => x.ServiceId == 2);
        var outside = rows.Single(x => x.ServiceId == 1);

        Assert.Equal((12L, 7L, 5L, 0L), (queue.Received, queue.Completed, queue.Pending, queue.AmountPaise));
        Assert.Equal(7d / 12 * 100, queue.DisposalPct!.Value, 6);
        Assert.Equal((3L, 0L, 0L, 30000L), (payments.Received, payments.Completed, payments.Pending, payments.AmountPaise));
        Assert.Null(payments.DisposalPct);
        Assert.Equal((0L, 0L, 0L, 0L), (outside.Received, outside.Completed, outside.Pending, outside.AmountPaise));
        Assert.Null(outside.DisposalPct);
    }

    /// Pending is a balance, not a flow: it is what stood at the end of the period's last day.
    [Fact]
    public async Task PendingIsTheClosingBalanceOnThePeriodsLastDayNotASumOverItsDays()
    {
        await using var f = await Filed(opening: id => id == 11 ? 40 : 0);

        var first = (await Services(f, new PeriodFilter(Day1, Day1))).Single(x => x.ServiceId == 11);
        var later = (await Services(f, new PeriodFilter(Day3, Day3))).Single(x => x.ServiceId == 11);

        Assert.Equal(40 + 5 - 2 + 40 + 3 - 1, first.Pending);
        Assert.Equal(0, later.Received);
        Assert.Equal(40 + 5 - 2 + 4 - 4 + 40 + 3 - 1, later.Pending);
    }

    [Fact]
    public async Task AnAnchalFilterNarrowsEveryServiceRow()
    {
        await using var f = await Filed();

        var rows = await Services(f, new PeriodFilter(Day1, Day3, f.OfficeId(2)));
        var queue = rows.Single(x => x.ServiceId == 11);

        Assert.Equal((3L, 1L, 2L), (queue.Received, queue.Completed, queue.Pending));
        Assert.Equal(0, rows.Single(x => x.ServiceId == 2).Received);
        Assert.Equal(50000, rows.Single(x => x.ServiceId == 1).AmountPaise);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheServiceRowsAddUpToTheAnchalTable(bool oneAnchal)
    {
        await using var f = await Filed(opening: id => id == 11 ? 40 : id == 1 ? 7 : 0);
        var filter = new PeriodFilter(Day1, Day3, oneAnchal ? f.OfficeId(1) : null);

        var services = await Services(f, filter);
        var anchals = await Anchals(f, filter);
        var queue = services.Where(x => MonitoringRules.MovesPendingBalance(x.ServiceId)).ToArray();

        Assert.Equal(anchals.Sum(x => x.Received), queue.Sum(x => x.Received));
        Assert.Equal(anchals.Sum(x => x.Completed), queue.Sum(x => x.Completed));
        Assert.Equal(anchals.Sum(x => x.Pending), services.Sum(x => x.Pending));
        Assert.Equal(anchals.Sum(x => x.AmountPaise), services.Sum(x => x.AmountPaise));
    }

    // ---- by day ----

    [Fact]
    public async Task ThereIsOneRowPerDayInOrderIncludingADayWithNothingFiled()
    {
        await using var f = await Fixture.Create();
        await f.Save(1, Input(11, Day1, received: 5, completed: 2));
        await f.Save(1, Input(11, Day3, received: 1, completed: 1));

        var rows = await Days(f, new PeriodFilter(Day1, Day3));

        Assert.Equal(new[] { Day1, Day2, Day3 }, rows.Select(x => x.Date));
        Assert.Equal((0L, 0L, 0L), (rows[1].Received, rows[1].Completed, rows[1].AmountPaise));
    }

    /// Received and disposed are the queue services'; a payment count is not an application. The amount is
    /// everything collected that day, fees and payments alike.
    [Fact]
    public async Task ADaysFiguresAreItsQueueServicesAndEverythingCollected()
    {
        await using var f = await Fixture.Create();
        await f.Save(1, Input(11, Day1, received: 5, completed: 2));
        await f.Save(1, Input(2, Day1, received: 3, amountPaise: 30000));
        await f.Save(2, Input(1, Day1, received: 2, completed: 1, amountPaise: 50000));

        var day = Assert.Single(await Days(f, new PeriodFilter(Day1, Day1)));

        Assert.Equal((7L, 3L, 80000L), (day.Received, day.Completed, day.AmountPaise));
    }

    [Fact]
    public async Task PinnedToOneServiceTheDaysAreThatServicesAlone()
    {
        await using var f = await Filed();

        var rows = await Days(f, new PeriodFilter(Day1, Day3, null, 1));

        Assert.Equal((0L, 0L, 0L), (rows[0].Received, rows[0].Completed, rows[0].AmountPaise));
        Assert.Equal((2L, 1L, 50000L), (rows[2].Received, rows[2].Completed, rows[2].AmountPaise));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(1, null)]
    [InlineData(null, 11)]
    [InlineData(2, 1)]
    public async Task TheDayRowsAddUpToTheAnchalTable(int? anchal, int? service)
    {
        await using var f = await Filed();
        var filter = new PeriodFilter(Day1, Day3, anchal is { } number ? f.OfficeId(number) : null, service);

        var days = await Days(f, filter);
        var anchals = await Anchals(f, filter);

        Assert.Equal(anchals.Sum(x => x.Received), days.Sum(x => x.Received));
        Assert.Equal(anchals.Sum(x => x.Completed), days.Sum(x => x.Completed));
        Assert.Equal(anchals.Sum(x => x.AmountPaise), days.Sum(x => x.AmountPaise));
    }

    [Fact]
    public async Task APeriodOfNinetyTwoDaysIsServedAndOneDayMoreIsRefused()
    {
        await using var f = await Fixture.Create();
        var last = new DateOnly(2026, 10, 5);

        var most = await f.Service.GetDashboardDaysAsync(f.Commissioner, new PeriodFilter(last.AddDays(-91), last));
        var over = await f.Service.GetDashboardDaysAsync(f.Commissioner, new PeriodFilter(last.AddDays(-92), last));

        Assert.Equal(MonitoringService.DayRowsMaximum, most.Value!.Data.Rows.Count);
        Assert.Equal(422, over.Status);
        Assert.Equal("Choose a period of 92 days or fewer", over.Error!.Message);
    }

    // ---- who may read them ----

    [Fact]
    public async Task AnOfficeAccountIsRefusedBoth()
    {
        await using var f = await Fixture.Create();
        var filter = new PeriodFilter(Day1, Day3);

        Assert.Equal(403, (await f.Service.GetDashboardServicesAsync(f.Office(1), filter)).Status);
        Assert.Equal(403, (await f.Service.GetDashboardDaysAsync(f.Office(1), filter)).Status);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public CommissionerMonitoringDbContext Db { get; } = new(new DbContextOptionsBuilder<CommissionerMonitoringDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        private readonly Guid[] offices = [Guid.NewGuid(), Guid.NewGuid()];
        private readonly Guid[] users = [Guid.NewGuid(), Guid.NewGuid()];
        private readonly Guid commissioner = Guid.NewGuid();
        private int requests;
        public MonitoringService Service => new(Db, new Clock());
        public ClaimsPrincipal Commissioner => PrincipalFor(commissioner, null);
        /// Anchal numbers here are 1 and 2.
        public Guid OfficeId(int number) => offices[number - 1];
        public ClaimsPrincipal Office(int number) => PrincipalFor(users[number - 1], offices[number - 1]);

        private static ClaimsPrincipal PrincipalFor(Guid id, Guid? officeId)
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, id.ToString()),
                new(MonitoringPolicies.UserIdClaim, id.ToString()),
                new(MonitoringPolicies.ScopeClaim, MonitoringPolicies.ScopeValue),
                new(MonitoringPolicies.RoleClaim, officeId.HasValue ? MonitoringPolicies.OfficeRole : MonitoringPolicies.CommissionerRole) };
            if (officeId.HasValue) claims.Add(new(MonitoringPolicies.OfficeIdClaim, officeId.ToString()!));
            return new(new ClaimsIdentity(claims, MonitoringPolicies.AuthenticationScheme));
        }

        public async Task Save(int office, EntryInput input)
        {
            var saved = await Service.SaveEntryAsync(Office(office), new($"entry-request-{++requests}", null, "backdate", input, "Late"));
            Assert.True(saved.IsSuccess, saved.Error?.Message);
        }

        public static async Task<Fixture> Create(Func<int, long>? opening = null)
        {
            var f = new Fixture();
            for (var i = 0; i < f.offices.Length; i++)
            {
                f.Db.Offices.Add(new MonitoringOffice { Id = f.offices[i], Code = $"anchal{i + 1}", Name = $"Anchal {i + 1}" });
                f.Db.Users.Add(new MonitoringUser { Id = f.users[i], LoginName = $"anchal{i + 1}", Role = MonitoringPolicies.OfficeRole });
                f.Db.UserOffices.Add(new MonitoringUserOffice { UserId = f.users[i], OfficeId = f.offices[i] });
            }
            f.Db.Users.Add(new MonitoringUser { Id = f.commissioner, LoginName = "commissioner", Role = MonitoringPolicies.CommissionerRole });
            f.Db.CatalogItems.AddRange(Enumerable.Range(1, MonitoringRules.ServiceCount).Select(x => new MonitoringCatalogItem {
                Id = Guid.NewGuid(), Key = x.ToString(), Name = $"Service {x}", Kind = MonitoringActivityKind.Service, SortOrder = x }));
            await f.Db.SaveChangesAsync();
            for (var i = 0; i < f.offices.Length; i++)
            {
                var setup = await f.Service.SaveSetupAsync(f.Office(i + 1), new($"setup-request-{i}", null, Day1,
                    Enumerable.Range(1, MonitoringRules.ServiceCount).Select(x => new OpeningBalanceDto(x, new(opening?.Invoke(x) ?? 0, 0, 0))).ToArray()));
                Assert.True(setup.IsSuccess, setup.Error?.Message);
            }
            return f;
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.Parse("2026-10-05T18:30:00Z");
    }
}
