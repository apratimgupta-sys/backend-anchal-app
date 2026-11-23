using System.Net;
using Anchal.Domain.CommissionerMonitoring.Rules;
using Xunit;

namespace Anchal.Tests.CommissionerMonitoring.FieldVisits;

/// A third role was added to a portal that had two. These tests pin which role
/// may call which route, so a later role addition cannot quietly widen access
/// to a route nobody thought about.
public sealed class RoleRouteMatrixTests
{
    [Fact]
    public async Task AFieldOfficerCannotReadTheirOfficesAuditHistory()
    {
        await using var host = await MonitoringTestHost.Create();
        await host.SignInAsOfficer();

        using var response = await host.Client.GetAsync(MonitoringTestHost.Root + "/history");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AFieldOfficerCannotReadTheCommissionerDashboard()
    {
        await using var host = await MonitoringTestHost.Create();
        await host.SignInAsOfficer();
        var today = MonitoringRules.Today(DateTimeOffset.UtcNow).ToString("yyyy-MM-dd");

        using var response = await host.Client.GetAsync(
            MonitoringTestHost.Root + $"/dashboard?from={today}&to={today}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AnOfficeAccountStillReadsItsOwnAuditHistory()
    {
        await using var host = await MonitoringTestHost.Create();
        await host.Login("anchal3");

        using var response = await host.Client.GetAsync(MonitoringTestHost.Root + "/history");

        Assert.True(response.IsSuccessStatusCode,
            $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task AFieldOfficerStillReadsTheServiceMaster()
    {
        await using var host = await MonitoringTestHost.Create();
        await host.SignInAsOfficer();

        using var response = await host.Client.GetAsync(MonitoringTestHost.Root + "/services");

        Assert.True(response.IsSuccessStatusCode,
            $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }
}

/// A password reset must end the officer's current session, not merely change
/// what they type next time. The role manager resets a password when a phone
/// is lost; a cookie that keeps working for up to the token lifetime makes
/// that instruction useless.
public sealed class PasswordResetRevokesSessionTests
{
    private static string Today() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata")).DateTime).ToString("yyyy-MM-dd");

    [Fact]
    public async Task AResetEndsTheOfficersCurrentSessionImmediately()
    {
        await using var host = await MonitoringTestHost.Create();
        var officer = await host.SignInAsOfficer();

        // The role manager resets it from their own client, as they would in life.
        using var admin = host.NewClient();
        await MonitoringTestHost.Login(admin, "rolemanager");
        using var reset = await admin.PostAsync(
            MonitoringTestHost.Root + $"/officers/{officer.GetProperty("id").GetString()}/password-reset",
            MonitoringTestHost.Json(new { }));
        Assert.True(reset.IsSuccessStatusCode,
            $"{reset.StatusCode}: {await reset.Content.ReadAsStringAsync()}");

        // The officer's existing cookie must no longer file visits.
        using var response = await host.Client.GetAsync(
            MonitoringTestHost.Root + $"/field-visits?from={Today()}&to={Today()}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("password_change_required", await response.Content.ReadAsStringAsync());
    }
}
