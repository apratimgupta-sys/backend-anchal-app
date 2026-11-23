using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Globalization;
using System.Security.Claims;
using Anchal.Application.CommissionerMonitoring.Auth;
using Anchal.Application.CommissionerMonitoring.DTOs;
using Anchal.Application.CommissionerMonitoring.Services;
using Anchal.Domain.CommissionerMonitoring;
using Anchal.Domain.CommissionerMonitoring.Rules;
using Anchal.Infrastructure.CommissionerMonitoring;
using Anchal.Infrastructure.CommissionerMonitoring.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Anchal.Tests.CommissionerMonitoring.FieldVisits;

/// The unique (OfficerUserId, RequestId) index is the only thing standing
/// between a double-tapped Submit and two records for one site visit, and the
/// InMemory provider the other tests use does not enforce unique indexes at
/// all. These run against a real Postgres, skipped when none is configured:
///   ANCHAL_TEST_POSTGRES="Host=127.0.0.1;Port=5433;Username=postgres;Password=postgres"
public sealed class FieldVisitRaceTests
{
    private static string? Admin => Environment.GetEnvironmentVariable("ANCHAL_TEST_POSTGRES");

    /// Deterministic version of losing the race: an interceptor commits another
    /// row under the same request id on a separate connection in the instant
    /// between this submission's existence check and its INSERT. The row it
    /// plants carries different data, so the officer must get the ordinary 409
    /// about a reused identifier — never a DbUpdateException, which the API
    /// turns into a 500 telling them to retry something that cannot succeed.
    [Fact]
    public async Task ASubmissionThatLosesTheRaceGetsARuleErrorRatherThanACrash()
    {
        if (string.IsNullOrWhiteSpace(Admin)) return;
        await using var database = await TestDatabase.Create(Admin!);
        var officer = await database.CreateOfficer();
        var requestId = Guid.NewGuid().ToString("N");
        database.OnInsertingVisit = () => database.InsertRowDirectly(requestId);

        var result = await database.Save(officer, requestId);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.Status);
        Assert.Equal("CONFLICT", result.Error!.Code);
        await using var reader = database.Context();
        Assert.Equal(1, await reader.FieldVisits.CountAsync(x => x.RequestId == requestId));
    }

    [Fact]
    public async Task ManyConcurrentSubmissionsOfOneRequestIdLeaveExactlyOneVisit()
    {
        if (string.IsNullOrWhiteSpace(Admin)) return;
        await using var database = await TestDatabase.Create(Admin!);
        var officer = await database.CreateOfficer();
        var requestId = Guid.NewGuid().ToString("N");

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            try { return (await database.Save(officer, requestId)).IsSuccess ? "ok" : "rule"; }
            catch (Exception error) { return error.GetType().Name; }
        }));

        await using var reader = database.Context();
        Assert.Equal(1, await reader.FieldVisits.CountAsync(x => x.RequestId == requestId));
        Assert.All(results, result => Assert.Equal("ok", result));
    }

    [Fact]
    public async Task ASequentialReplayStillReturnsTheFirstVisit()
    {
        if (string.IsNullOrWhiteSpace(Admin)) return;
        await using var database = await TestDatabase.Create(Admin!);
        var officer = await database.CreateOfficer();
        var requestId = Guid.NewGuid().ToString("N");

        var first = await database.Save(officer, requestId);
        var second = await database.Save(officer, requestId);

        Assert.True(first.IsSuccess, first.Error?.Message ?? "no error");
        Assert.True(second.IsSuccess, second.Error?.Message ?? "no error");
        Assert.Equal(first.Value!.Id, second.Value!.Id);
    }

    private sealed class TestDatabase(string admin, string name) : IAsyncDisposable
    {
        private ServiceProvider _provider = null!;

        /// Fires once, just before a field visit INSERT is sent.
        public Action? OnInsertingVisit { get; set; }

        public void InsertRowDirectly(string requestId)
        {
            using var scope = _provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CommissionerMonitoringDbContext>();
            var office = db.Offices.Single(x => x.Code == "anchal3");
            var officer = db.Users.Single(x => x.LoginName == "je.race");
            db.FieldVisits.Add(new MonitoringFieldVisit
            {
                Id = Guid.NewGuid(), RequestId = requestId, Fingerprint = "other",
                OfficeId = office.Id, OfficerUserId = officer.Id, Ward = 24, ServiceId = 9,
                VisitDate = MonitoringRules.Today(_captured), VisitTime = new(10, 30),
                Latitude = 23.3441, Longitude = 85.3096, AccuracyMetres = 18,
                GpsCapturedAt = _captured, ReceivedAt = _captured, VerifiedOnSite = true,
                IssueAction = "Fogging done along the main lane; two bylanes still pending."
            });
            db.SaveChanges();
        }

        public static async Task<TestDatabase> Create(string admin)
        {
            var name = "anchal_race_" + Guid.NewGuid().ToString("N")[..12];
            await using (var connection = new NpgsqlConnection(admin + ";Database=postgres"))
            {
                await connection.OpenAsync();
                await using var create = new NpgsqlCommand($"create database \"{name}\"", connection);
                await create.ExecuteNonQueryAsync();
            }
            TestDatabase? database = null;
            database = new TestDatabase(admin, name);
            database._provider = new ServiceCollection()
                .AddDbContext<CommissionerMonitoringDbContext>(x => x.UseNpgsql(
                    admin + ";Database=" + name,
                    npgsql => npgsql.MigrationsHistoryTable(
                        "__monitoring_ef_migrations_history", CommissionerMonitoringDbContext.Schema))
                    .AddInterceptors(new VisitInsertInterceptor(() => database!.TakeHook())))
                .AddCommissionerMonitoringWorkflows()
                .AddScoped<Anchal.Application.CommissionerMonitoring.Auth.IMonitoringProvisioner,
                    Anchal.Infrastructure.CommissionerMonitoring.Seeding.MonitoringProvisioner>()
                .AddSingleton(TimeProvider.System)
                .BuildServiceProvider();
            await using (var scope = database._provider.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<CommissionerMonitoringDbContext>()
                    .Database.MigrateAsync();
                await scope.ServiceProvider.GetRequiredService<IMonitoringProvisioner>().ProvisionAsync(
                    Enumerable.Range(1, 6).Select(i => "anchal" + i).Append("commissioner").Append("rolemanager")
                        .ToDictionary(x => x, _ => "Race-test-password-123!"));
            }
            return database;
        }

        /// Returns the hook once and clears it, so the forced collision happens
        /// on the first INSERT only and the row it inserts does not recurse.
        public Action? TakeHook()
        {
            var hook = OnInsertingVisit;
            OnInsertingVisit = null;
            return hook;
        }

        public CommissionerMonitoringDbContext Context() =>
            _provider.GetRequiredService<IServiceScopeFactory>().CreateScope()
                .ServiceProvider.GetRequiredService<CommissionerMonitoringDbContext>();

        public async Task<ClaimsPrincipal> CreateOfficer()
        {
            await using var scope = _provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CommissionerMonitoringDbContext>();
            var office = await db.Offices.SingleAsync(x => x.Code == "anchal3");
            var user = new MonitoringUser
            {
                Id = Guid.NewGuid(),
                LoginName = "je.race",
                NormalizedLoginName = "je.race",
                DisplayName = "R. Officer",
                Designation = "Junior Engineer",
                Role = MonitoringPolicies.FieldOfficerRole,
                PasswordHash = "x",
                IsActive = true,
                MustChangePassword = false,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.Users.Add(user);
            db.UserOffices.Add(new MonitoringUserOffice
            {
                UserId = user.Id, OfficeId = office.Id, AssignedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
            return new(new ClaimsIdentity(
            [
                new Claim(MonitoringPolicies.ScopeClaim, MonitoringPolicies.ScopeValue),
                new Claim(MonitoringPolicies.UserIdClaim, user.Id.ToString()),
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(MonitoringPolicies.RoleClaim, MonitoringPolicies.FieldOfficerRole),
                new Claim(MonitoringPolicies.OfficeIdClaim, office.Id.ToString())
            ], "test"));
        }

        // One fixed reading for the whole fixture: a replay is only a replay if
        // the payload is identical, and a per-call UtcNow is not.
        private readonly DateTimeOffset _captured = DateTimeOffset.UtcNow;

        public async Task<MonitoringResult<FieldVisitDto>> Save(ClaimsPrincipal officer, string requestId)
        {
            await using var scope = _provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IMonitoringFieldVisitService>()
                .CreateAsync(officer, new SaveFieldVisitCommand(requestId, new FieldVisitInputDto(
                    MonitoringRules.Today(_captured), "10:30", 3, 24, 9,
                    new GpsReadingDto(23.3441, 85.3096, 18, _captured),
                    "Fogging done along the main lane; two bylanes still pending.")), default);
        }

        public async ValueTask DisposeAsync()
        {
            await _provider.DisposeAsync();
            NpgsqlConnection.ClearAllPools();
            await using var connection = new NpgsqlConnection(admin + ";Database=postgres");
            await connection.OpenAsync();
            await using var drop = new NpgsqlCommand(
                $"drop database if exists \"{name}\" with (force)", connection);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private sealed class VisitInsertInterceptor(Func<Action?> hook) : DbCommandInterceptor
    {
        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            Fire(command);
            return base.NonQueryExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken ct = default)
        {
            Fire(command);
            return base.NonQueryExecutingAsync(command, eventData, result, ct);
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Fire(command);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken ct = default)
        {
            Fire(command);
            return base.ReaderExecutingAsync(command, eventData, result, ct);
        }

        private void Fire(DbCommand command)
        {
            if (!command.CommandText.Contains("INSERT INTO commissioner_monitoring.monitoring_field_visits",
                StringComparison.Ordinal)) return;
            hook()?.Invoke();
        }
    }
}
