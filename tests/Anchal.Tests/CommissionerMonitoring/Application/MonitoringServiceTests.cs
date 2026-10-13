using System.Security.Claims;
using System.Text.Json;
using Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Anchal.Application.CommissionerMonitoring.Auth;
using Anchal.Application.CommissionerMonitoring.DTOs;
using Anchal.Application.CommissionerMonitoring.Services;
using Anchal.Domain.CommissionerMonitoring;
using Anchal.Domain.CommissionerMonitoring.Rules;
using Anchal.Infrastructure.CommissionerMonitoring;
using Anchal.Infrastructure.CommissionerMonitoring.Persistence;

namespace Anchal.Tests.CommissionerMonitoring.Application;

public sealed class MonitoringServiceTests
{
    [Fact]
    public async Task SessionFreshnessIsStableAcrossPollsAndChangesWithSessionData()
    {
        await using var f = await Fixture.Create();
        var first = (await f.Service.GetSessionAsync(f.Principal)).Value!;
        f.Clock.Advance(TimeSpan.FromMinutes(1));
        var unchanged = (await f.Service.GetSessionAsync(f.Principal)).Value!;
        Assert.Equal(first.LastUpdated, unchanged.LastUpdated);
        Assert.True(unchanged.RetrievedAt > first.RetrievedAt);
        Assert.Null(first.LastUpdated); // No persisted modification timestamp exists for this data.
        Assert.False(string.IsNullOrEmpty(first.Revision));
        Assert.Equal(first.Revision, unchanged.Revision);

        var user = await f.Db.Users.SingleAsync(x => x.Id == f.User);
        user.LoginName = "renamed-office";
        await f.Db.SaveChangesAsync();
        var changed = (await f.Service.GetSessionAsync(f.Principal)).Value!;
        Assert.Equal("renamed-office", changed.Data.Username);
        Assert.NotEqual(first.Revision, changed.Revision);
        Assert.Equal(unchanged.RetrievedAt, changed.RetrievedAt); // Changes are detected without clock movement.
        Assert.Equal(changed.Revision, (await f.Service.GetSessionAsync(f.Principal)).Value!.Revision);
    }

    [Fact]
    public async Task CatalogFreshnessIsStableAcrossPollsAndChangesWithCatalogData()
    {
        await using var f = await Fixture.Create();
        var first = (await f.Service.GetServicesAsync(f.Principal)).Value!;
        f.Clock.Advance(TimeSpan.FromMinutes(1));
        var unchanged = (await f.Service.GetServicesAsync(f.Principal)).Value!;
        Assert.Equal(first.LastUpdated, unchanged.LastUpdated);
        Assert.True(unchanged.RetrievedAt > first.RetrievedAt);
        Assert.Null(first.LastUpdated);
        Assert.False(string.IsNullOrEmpty(first.Revision));
        Assert.Equal(first.Revision, unchanged.Revision);

        var item = await f.Db.CatalogItems.SingleAsync(x => x.Key == "1");
        item.Name = "Renamed service";
        await f.Db.SaveChangesAsync();
        var renamed = (await f.Service.GetServicesAsync(f.Principal)).Value!;
        Assert.Equal("Renamed service", renamed.Data.Single(x => x.Id == 1).Name);
        Assert.NotEqual(first.Revision, renamed.Revision);
        item.IsActive = false;
        await f.Db.SaveChangesAsync();
        var disabled = (await f.Service.GetServicesAsync(f.Principal)).Value!;
        Assert.DoesNotContain(disabled.Data, x => x.Id == 1);
        Assert.NotEqual(renamed.Revision, disabled.Revision);
        Assert.Equal(disabled.Revision, (await f.Service.GetServicesAsync(f.Principal)).Value!.Revision);
    }

    [Fact]
    public async Task DashboardSummaryAndDetailExcludeInactiveOffices()
    {
        await using var f = await Fixture.Create();
        await f.Setup();
        await f.Service.SaveEntryAsync(f.Principal, Save(new(2026, 10, 6)));
        var other = f.PrincipalFor(f.OtherUser, f.OtherOffice);
        Assert.True((await f.Service.SaveSetupAsync(other, Setup())).IsSuccess);
        Assert.True((await f.Service.SaveEntryAsync(other, Save(new(2026, 10, 6)))).IsSuccess);
        var office = await f.Db.Offices.SingleAsync(x => x.Id == f.OtherOffice);
        office.IsActive = false;
        await f.Db.SaveChangesAsync();

        var filter = new PeriodFilter(new(2026, 10, 6), new(2026, 10, 6));
        var summary = (await f.Service.GetDashboardAsync(f.Commissioner, filter)).Value!.Data;
        var detail = (await f.Service.GetDashboardEntriesAsync(f.Commissioner, filter)).Value!.Data;
        Assert.Equal(f.Office, Assert.Single(summary.Rows).OfficeId);
        Assert.Equal(f.Office, Assert.Single(detail).OfficeId);
        Assert.Equal(422, (await f.Service.GetDashboardAsync(f.Commissioner,
            filter with { OfficeId = f.OtherOffice })).Status);
        Assert.Equal(422, (await f.Service.GetDashboardEntriesAsync(f.Commissioner,
            filter with { OfficeId = f.OtherOffice })).Status);
        Assert.Single((await f.Service.GetDashboardEntriesAsync(f.Commissioner,
            filter with { OfficeId = f.Office })).Value!.Data);
    }

    [Fact]
    public void ApplicationServiceResolvesWithDedicatedContext()
    {
        var services = new ServiceCollection();
        services.AddDbContext<CommissionerMonitoringDbContext>(x => x.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddCommissionerMonitoringWorkflows();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.IsType<MonitoringService>(scope.ServiceProvider.GetRequiredService<IMonitoringService>());
        Assert.Same(scope.ServiceProvider.GetRequiredService<CommissionerMonitoringDbContext>(),
            scope.ServiceProvider.GetRequiredService<IMonitoringContext>());
    }

    [Fact]
    public async Task SessionCarriesJwtExpiryInCloudflareMilliseconds()
    {
        await using var f = await Fixture.Create();
        var principal = f.Principal;
        ((ClaimsIdentity)principal.Identity!).AddClaim(new("exp", "1791309600"));
        Assert.Equal(1791309600000, (await f.Service.GetSessionAsync(principal)).Value!.Data.ExpiresAt);
    }

    [Fact]
    public async Task NonfiniteDaysReturnValidationInsteadOfSerializationFailure()
    {
        await using var f = await Fixture.Create();
        await f.Setup();
        var command = Save(new(2026, 10, 6));
        var result = await f.Service.SaveEntryAsync(f.Principal,
            command with { Input = command.Input with { AvgDisposalDays = double.NaN } });
        Assert.Equal(422, result.Status);
        Assert.Contains("avgDisposalDays", result.Error!.FieldErrors!.Keys);
        Assert.Empty(await f.Db.Reports.ToListAsync());
    }

    [Fact]
    public async Task FailedSaveDiscardsTrackedMutationAndDoesNotCreateAuditOrReceipt()
    {
        var interceptor = new FailSave();
        await using var f = await Fixture.Create(interceptor);
        await f.Setup();
        var command = Save(new(2026, 10, 6));
        interceptor.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.SaveEntryAsync(f.Principal, command));
        Assert.Empty(f.Db.ChangeTracker.Entries());
        Assert.Empty(await f.Db.Reports.ToListAsync());
        Assert.Single(await f.Db.AuditEvents.ToListAsync());
        interceptor.Fail = false;
        Assert.True((await f.Service.SaveEntryAsync(f.Principal, command)).IsSuccess);
        Assert.Equal(2, await f.Db.AuditEvents.CountAsync());
    }

    [Fact]
    public async Task UnconfiguredOfficeAndOutOfRouteReadsAndWritesAreRejected()
    {
        await using var f = await Fixture.Create();
        Assert.Equal(422, (await f.Service.SaveEntryAsync(f.Principal, Save(new(2026, 10, 6)))).Status);
        Assert.Equal(422, (await f.Service.GetEntriesAsync(f.Principal, "today", new(2026, 10, 6))).Status);
        await f.Setup();
        foreach (var (mode, date) in new[] { ("today", new DateOnly(2026, 10, 5)),
            ("backdate", new DateOnly(2026, 10, 6)), ("backdate", new DateOnly(2026, 9, 30)) })
        {
            Assert.Equal(422, (await f.Service.GetEntriesAsync(f.Principal, mode, date)).Status);
            Assert.Equal(422, (await f.Service.SaveEntryAsync(f.Principal, Save(date, mode, "Late"))).Status);
        }
        Assert.Equal(422, (await f.Service.SaveEntryAsync(f.Principal,
            Save(new(2026, 10, 6)) with { RequestId = "bad" })).Status);
        Assert.Empty(await f.Db.Reports.ToListAsync());
    }

    [Fact]
    public async Task UnrelatedOfficeChangesDoNotAdvanceOfficeFreshness()
    {
        await using var f = await Fixture.Create();
        await f.Setup();
        var first = (await f.Service.GetHistoryAsync(f.Principal)).Value!;
        f.Clock.Advance(TimeSpan.FromMinutes(1));
        await f.Service.SaveSetupAsync(f.PrincipalFor(f.OtherUser, f.OtherOffice), Setup());
        var next = (await f.Service.GetHistoryAsync(f.Principal)).Value!;
        Assert.Equal(first.LastUpdated, next.LastUpdated);
        Assert.True(next.RetrievedAt > first.RetrievedAt);
        Assert.Equal(f.Clock.GetUtcNow(), (await f.Service.GetHistoryAsync(f.Commissioner)).Value!.LastUpdated);
    }

    [Fact]
    public async Task ClaimsCannotBeCombinedAcrossIdentitiesOrDuplicated()
    {
        await using var f = await Fixture.Create();
        var principal = f.Principal;
        ((ClaimsIdentity)principal.Identity!).AddClaim(new(MonitoringPolicies.OfficeIdClaim, f.OtherOffice.ToString()));
        Assert.Equal(403, (await f.Service.GetSetupAsync(principal)).Status);
        var split = new ClaimsPrincipal(new[] {
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, f.User.ToString())], "legacy"),
            new ClaimsIdentity([new Claim(MonitoringPolicies.ScopeClaim, MonitoringPolicies.ScopeValue)]) });
        Assert.Equal(401, (await f.Service.GetSessionAsync(split)).Status);
    }

    [Fact]
    public async Task SetupLocksAllBalancesAndAppendsOneAuditWithReplay()
    {
        await using var f = await Fixture.Create();
        var command = Setup();
        var saved = await f.Service.SaveSetupAsync(f.Principal, command);
        Assert.True(saved.IsSuccess);
        Assert.Equal(25, saved.Value!.Balances.Count);
        Assert.Equal(75, await f.Db.OpeningBalances.CountAsync());
        Assert.Equivalent(saved.Value, (await f.Service.SaveSetupAsync(f.Principal, command)).Value);
        Assert.Single(await f.Db.AuditEvents.ToListAsync());
        Assert.Equal(409, (await f.Service.SaveSetupAsync(f.Principal,
            command with { RequestId = "different-setup" })).Status);
        var read = await f.Service.GetSetupAsync(f.Principal);
        Assert.Equal(f.Clock.GetUtcNow(), read.Value!.LastUpdated);
        Assert.Equal(1, read.Value.Data!.Version);
    }

    [Fact]
    public async Task IncompleteOrFutureSetupNeverWrites()
    {
        await using var f = await Fixture.Create();
        foreach (var command in new[] {
            Setup() with { Balances = Setup().Balances.Take(24).ToArray() },
            Setup() with { StartDate = new(2026, 10, 7) },
            Setup() with { ExpectedVersion = 1 },
            Setup() with { Balances = Enumerable.Repeat(new OpeningBalanceDto(1, new(0, 0, 0)), 25).ToArray() },
            Setup() with { Balances = Setup().Balances.Select(x => x.ServiceId == 1 ? x with { Opening = new(-1, 0, 0) } : x).ToArray() } })
            Assert.False((await f.Service.SaveSetupAsync(f.Principal, command)).IsSuccess);
        Assert.Empty(await f.Db.OfficeSetups.ToListAsync());
        Assert.Empty(await f.Db.AuditEvents.ToListAsync());
    }

    [Fact]
    public async Task ZeroSubmissionAndAnotherServiceRemainIndependent()
    {
        await using var f = await Fixture.Create();
        await f.Setup();
        var command = Save(new(2026, 10, 6));
        var saved = (await f.Service.SaveEntryAsync(f.Principal, command)).Value!;
        Assert.Equal(new Balances(10, 3, 5), saved.Closing);
        Assert.Equal(new DateOnly(2026, 10, 6), saved.ReportingDate);
        Assert.Equal(1, saved.Version);
        Assert.True((await f.Db.Reports.SingleAsync()).IsZeroActivity);
        var second = await f.Service.SaveEntryAsync(f.Principal, command with {
            RequestId = "service-two", Input = command.Input with { ServiceId = 2, Received = 3 } });
        Assert.True(second.IsSuccess);
        Assert.False((await f.Db.Reports.SingleAsync()).IsZeroActivity);
        Assert.Equal(6, await f.Db.ReportEntries.CountAsync());
        var read = (await f.Service.GetEntriesAsync(f.Principal, "today", new(2026, 10, 6))).Value!;
        Assert.Equal(2, read.Data.Entries.Count);
        Assert.Equal(25, read.Data.Openings.Count);
        Assert.Equal(f.Clock.GetUtcNow(), read.LastUpdated);
    }

    [Fact]
    public async Task CorrectionRequiresVersionAndReasonAndKeepsImmutableRegistration()
    {
        await using var f = await Fixture.Create();
        await f.Setup();
        var command = Save(new(2026, 10, 5), "backdate", "Late register");
        Assert.Equal(422, (await f.Service.SaveEntryAsync(f.Principal, command with { Reason = null })).Status);
        var original = (await f.Service.SaveEntryAsync(f.Principal, command)).Value!;
        var originalAudit = (await f.Db.AuditEvents.SingleAsync(x => x.Action == "Entry submitted")).AfterJson;
        f.Clock.Advance(TimeSpan.FromDays(1));
        var correction = command with { RequestId = "correct-entry", ExpectedVersion = 1,
            Reason = "Register reconciled", Input = command.Input with { Received = 2, Ward = " 2 ", Remarks = " note " } };
        Assert.Equal(409, (await f.Service.SaveEntryAsync(f.Principal,
            correction with { ExpectedVersion = null }, original.Id)).Status);
        Assert.Equal(422, (await f.Service.SaveEntryAsync(f.Principal,
            correction with { Reason = " " }, original.Id)).Status);
        Assert.Equal(422, (await f.Service.SaveEntryAsync(f.Principal,
            correction with { Input = correction.Input with { ServiceId = 2 } }, original.Id)).Status);
        var saved = (await f.Service.SaveEntryAsync(f.Principal, correction, original.Id)).Value!;
        Assert.Equal(2, saved.Version);
        Assert.Equal(original.ReportingDate, saved.ReportingDate);
        Assert.Equal(original.CreatedAt, saved.CreatedAt);
        Assert.Equal(f.Clock.GetUtcNow(), saved.UpdatedAt);
        Assert.Equal("2", saved.Input.Ward);
        Assert.Equal("note", saved.Input.Remarks);
        Assert.Equal(original.CreatedAt, (await f.Db.Reports.SingleAsync()).SubmittedAt);
        Assert.Equal(originalAudit, (await f.Db.AuditEvents.SingleAsync(x => x.Action == "Entry submitted")).AfterJson);
        Assert.Equal("Register reconciled", (await f.Db.Corrections.SingleAsync()).Reason);
        var history = (await f.Service.GetHistoryAsync(f.Principal)).Value!;
        Assert.Contains(history.Data, x => x.Reason == "Late register");
        Assert.Contains(history.Data, x => x.Reason == "Register reconciled");
    }

    [Fact]
    public async Task HistoricalChangeRejectsLaterNegativeAndRecomputesValidLaterClosings()
    {
        await using var f = await Fixture.Create();
        await f.Setup();
        var first = Save(new(2026, 10, 1), "backdate", "Late") with {
            Input = Input(new(2026, 10, 1)) with { Received = 2, Completed = 4, AvgDisposalDays = 2 } };
        var original = (await f.Service.SaveEntryAsync(f.Principal, first)).Value!;
        var later = Save(new(2026, 10, 3), "backdate", "Late") with { RequestId = "later-entry",
            Input = Input(new(2026, 10, 3)) with { Completed = 8, AvgDisposalDays = 2 } };
        Assert.True((await f.Service.SaveEntryAsync(f.Principal, later)).IsSuccess);
        var correction = first with { RequestId = "correct-first", ExpectedVersion = 1, Reason = "Reconciled",
            Input = first.Input with { Completed = 5 } };
        var failure = await f.Service.SaveEntryAsync(f.Principal, correction, original.Id);
        Assert.Equal(422, failure.Status);
        Assert.Equal(new DateOnly(2026, 10, 3), failure.Error!.ConflictDate);
        Assert.Equal(3, await f.Db.AuditEvents.CountAsync());
        Assert.Empty(await f.Db.Corrections.ToListAsync());
        Assert.Equal(4, (await f.Db.ReportEntries.SingleAsync(x => x.Id == original.Id)).DisposedCount);
        Assert.True((await f.Service.SaveEntryAsync(f.Principal,
            correction with { Input = first.Input with { Completed = 3 } }, original.Id)).IsSuccess);
        var read = (await f.Service.GetEntriesAsync(f.Principal, "backdate", new(2026, 10, 3))).Value!;
        Assert.Equal(1, Assert.Single(read.Data.Entries).Closing.Service);
        var laterReport = await f.Db.Reports.SingleAsync(x => x.ReportingDate == new DateOnly(2026, 10, 3));
        Assert.Equal(1, (await f.Db.ReportEntries.SingleAsync(x => x.ReportId == laterReport.Id && x.Kind == MonitoringActivityKind.Service)).ClosingBalance);
    }

    [Fact]
    public async Task LaterCorrectionAuditsCurrentBalancesAfterAnEarlierCorrection()
    {
        await using var f = await Fixture.Create();
        await f.Setup();
        var firstCommand = Save(new(2026, 10, 1), "backdate", "Late") with {
            RequestId = "first-entry", Input = Input(new(2026, 10, 1)) with { Received = 2 } };
        var first = (await f.Service.SaveEntryAsync(f.Principal, firstCommand)).Value!;
        var laterCommand = Save(new(2026, 10, 3), "backdate", "Late") with {
            RequestId = "later-entry", Input = Input(new(2026, 10, 3)) with { Received = 1 } };
        var later = (await f.Service.SaveEntryAsync(f.Principal, laterCommand)).Value!;

        Assert.True((await f.Service.SaveEntryAsync(f.Principal, firstCommand with {
            RequestId = "correct-first", ExpectedVersion = first.Version, Reason = "Reconciled",
            Input = firstCommand.Input with { Received = 4 } }, first.Id)).IsSuccess);
        Assert.True((await f.Service.SaveEntryAsync(f.Principal, laterCommand with {
            RequestId = "correct-later", ExpectedVersion = later.Version, Reason = "Clarified",
            Input = laterCommand.Input with { Remarks = "No count changes" } }, later.Id)).IsSuccess);

        var correction = await f.Db.Corrections.SingleAsync(x => x.EntryId == later.Id);
        using var before = JsonDocument.Parse(correction.BeforeJson);
        Assert.Equal(14, before.RootElement.GetProperty("opening").GetProperty("service").GetInt64());
        Assert.Equal(15, before.RootElement.GetProperty("closing").GetProperty("service").GetInt64());
        var audit = await f.Db.AuditEvents.SingleAsync(x => x.Action == "Entry corrected" && x.AggregateId == later.Id);
        using var auditedBefore = JsonDocument.Parse(audit.BeforeJson);
        Assert.Equal(14, auditedBefore.RootElement.GetProperty("opening").GetProperty("service").GetInt64());
        Assert.Equal(15, auditedBefore.RootElement.GetProperty("closing").GetProperty("service").GetInt64());
    }

    [Fact]
    public async Task RequestReplayReturnsOriginalResultAndRejectsChangedPayload()
    {
        await using var f = await Fixture.Create();
        await f.Setup();
        var command = Save(new(2026, 10, 6));
        var original = (await f.Service.SaveEntryAsync(f.Principal, command)).Value!;
        await f.Service.SaveEntryAsync(f.Principal, command with { RequestId = "correction-2",
            ExpectedVersion = 1, Reason = "Corrected", Input = command.Input with { Received = 1 } });
        Assert.Equal(original, (await f.Service.SaveEntryAsync(f.Principal, command)).Value);
        Assert.Equal(409, (await f.Service.SaveEntryAsync(f.Principal,
            command with { Input = command.Input with { Received = 4 } })).Status);
        Assert.Equal(3, await f.Db.AuditEvents.CountAsync());
    }

    [Fact]
    public async Task PrincipalAndDatabaseAssignmentScopeEveryOperation()
    {
        await using var f = await Fixture.Create();
        await f.Setup();
        var other = f.PrincipalFor(f.OtherUser, f.OtherOffice);
        Assert.Null((await f.Service.GetSetupAsync(other)).Value!.Data);
        Assert.Empty((await f.Service.GetHistoryAsync(other)).Value!.Data);
        var saved = (await f.Service.SaveEntryAsync(f.Principal, Save(new(2026, 10, 6)))).Value!;
        await f.Service.SaveSetupAsync(other, Setup());
        Assert.Equal(404, (await f.Service.SaveEntryAsync(other, Save(new(2026, 10, 6)), saved.Id)).Status);
        Assert.Equal(403, (await f.Service.GetDashboardAsync(f.Principal, new(new(2026, 10, 1), new(2026, 10, 6)))).Status);
        Assert.Equal(403, (await f.Service.SaveSetupAsync(f.Commissioner, Setup())).Status);
        var spoofed = f.PrincipalFor(f.User, f.OtherOffice);
        Assert.Equal(403, (await f.Service.GetSetupAsync(spoofed)).Status);
        Assert.Equal(401, (await f.Service.GetServicesAsync(new ClaimsPrincipal())).Status);
        var user = await f.Db.Users.SingleAsync(x => x.Id == f.User);
        user.IsActive = false;
        await f.Db.SaveChangesAsync();
        Assert.Equal(403, (await f.Service.GetSessionAsync(f.Principal)).Status);
    }

    [Fact]
    public async Task SessionCatalogDashboardAndHistoryExposeScopedFreshnessAndCorrectTotals()
    {
        await using var f = await Fixture.Create();
        Assert.Equal("office", (await f.Service.GetSessionAsync(f.Principal)).Value!.Data.Role);
        Assert.Equal(25, (await f.Service.GetServicesAsync(f.Principal)).Value!.Data.Count);
        await f.Setup();
        var command = Save(new(2026, 10, 1), "backdate", "Late") with { Input = Input(new(2026, 10, 1)) with {
            Received = 2, Completed = 4, SlaWithinTime = 4, AvgDisposalDays = 2, AmountPaise = 101,
            ComplaintsReceived = 1, ComplaintsResolved = 2, FieldCompleted = 1 } };
        await f.Service.SaveEntryAsync(f.Principal, command);
        var filter = new PeriodFilter(new(2026, 10, 1), new(2026, 10, 3));
        var dashboard = (await f.Service.GetDashboardAsync(f.Commissioner, filter)).Value!;
        var row = Assert.Single(dashboard.Data.Rows, x => x.OfficeId == f.Office);
        Assert.Equal(8, row.Pending);
        Assert.Equal(2, row.ComplaintsPending);
        Assert.Equal(4, row.FieldPending);
        Assert.Equal(200, row.DisposalPct);
        Assert.Equal(100, row.SlaPct);
        Assert.Equal(101, row.AmountPaise);
        Assert.Equal(75, row.Expected);
        Assert.Equal(1, row.Submitted);
        Assert.Equal(f.Clock.GetUtcNow(), dashboard.LastUpdated);
        var unconfigured = Assert.Single(dashboard.Data.Rows, x => x.OfficeId == f.OtherOffice);
        Assert.Null(unconfigured.DisposalPct);
        Assert.Equal(0, unconfigured.Expected);
        Assert.Contains("Initial balances not configured", unconfigured.ReviewReasons);
        Assert.Single((await f.Service.GetDashboardEntriesAsync(f.Commissioner, filter)).Value!.Data);
        var before = (await f.Service.GetDashboardAsync(f.Commissioner,
            new(new(2026, 9, 1), new(2026, 9, 30), f.Office))).Value!.Data.Rows.Single();
        Assert.Equal(0, before.Pending);
        Assert.Equal(0, before.Expected);
        Assert.Equal(422, (await f.Service.GetDashboardAsync(f.Commissioner,
            new(new(2026, 10, 6), new(2026, 10, 7)))).Status);
    }

    private static SetupCommand Setup() => new("setup-request", null, new(2026, 10, 1),
        Enumerable.Range(1, 25).Select(x => new OpeningBalanceDto(x, x == 1 ? new(10, 3, 5) : new(0, 0, 0))).ToArray());
    private static EntryInput Input(DateOnly date) => new(1, date, "1", 0, 0, 0, 0, 0, 0, 0, 0, null, "");
    private static SaveEntryCommand Save(DateOnly date, string mode = "today", string? reason = null) =>
        new("entry-request", null, mode, Input(date), reason);

    private sealed class FixedClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.Parse("2026-10-05T18:30:00Z");
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan amount) => now += amount;
    }

    private sealed class FailSave : SaveChangesInterceptor
    {
        public bool Fail { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            Fail ? throw new InvalidOperationException("Injected persistence failure") : ValueTask.FromResult(result);
    }

    private sealed class Fixture(FailSave? interceptor = null) : IAsyncDisposable
    {
        public CommissionerMonitoringDbContext Db { get; } = new(new DbContextOptionsBuilder<CommissionerMonitoringDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).AddInterceptors(interceptor ?? new FailSave()).Options);
        public FixedClock Clock { get; } = new();
        public Guid Office { get; } = Guid.NewGuid();
        public Guid OtherOffice { get; } = Guid.NewGuid();
        public Guid User { get; } = Guid.NewGuid();
        public Guid OtherUser { get; } = Guid.NewGuid();
        public Guid CommissionerId { get; } = Guid.NewGuid();
        public MonitoringService Service => new(Db, Clock);
        public ClaimsPrincipal Principal => PrincipalFor(User, Office);
        public ClaimsPrincipal Commissioner => PrincipalFor(CommissionerId, null);
        public ClaimsPrincipal PrincipalFor(Guid user, Guid? office)
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, user.ToString()),
                new(MonitoringPolicies.UserIdClaim, user.ToString()),
                new(MonitoringPolicies.ScopeClaim, MonitoringPolicies.ScopeValue),
                new(MonitoringPolicies.RoleClaim, office.HasValue ? MonitoringPolicies.OfficeRole : MonitoringPolicies.CommissionerRole) };
            if (office.HasValue) claims.Add(new(MonitoringPolicies.OfficeIdClaim, office.ToString()!));
            return new(new ClaimsIdentity(claims, MonitoringPolicies.AuthenticationScheme));
        }
        public async Task Setup() => Assert.True((await Service.SaveSetupAsync(Principal, MonitoringServiceTests.Setup())).IsSuccess);
        public static async Task<Fixture> Create(FailSave? interceptor = null)
        {
            var f = new Fixture(interceptor);
            f.Db.Offices.AddRange(new MonitoringOffice { Id = f.Office, Code = "anchal1", Name = "Anchal 1" },
                new MonitoringOffice { Id = f.OtherOffice, Code = "anchal2", Name = "Anchal 2" });
            f.Db.Users.AddRange(new MonitoringUser { Id = f.User, LoginName = "anchal1", Role = MonitoringPolicies.OfficeRole },
                new MonitoringUser { Id = f.OtherUser, LoginName = "anchal2", Role = MonitoringPolicies.OfficeRole },
                new MonitoringUser { Id = f.CommissionerId, LoginName = "commissioner", Role = MonitoringPolicies.CommissionerRole });
            f.Db.UserOffices.AddRange(new MonitoringUserOffice { UserId = f.User, OfficeId = f.Office },
                new MonitoringUserOffice { UserId = f.OtherUser, OfficeId = f.OtherOffice });
            f.Db.CatalogItems.AddRange(Enumerable.Range(1, 25).Select(x => new MonitoringCatalogItem {
                Id = Guid.NewGuid(), Key = x.ToString(), Name = $"Service {x}", Kind = MonitoringActivityKind.Service, SortOrder = x }));
            await f.Db.SaveChangesAsync();
            return f;
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
