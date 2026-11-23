using System.Net;
using System.Text.Json;
using Anchal.Infrastructure.CommissionerMonitoring.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Anchal.Tests.CommissionerMonitoring.FieldVisits;

/// Every session role may rotate its own password, and only its own. Without this the seeded
/// commissioner and role-manager credentials could never be rotated when someone with access leaves.
public sealed class AdministratorOwnPasswordTests
{
    private const string Root = MonitoringTestHost.Root;
    private const string Fresh = "Rotated#2026ab";

    private static Task<HttpResponseMessage> Change(HttpClient http, string current, string next) =>
        http.PostAsync(Root + "/password", MonitoringTestHost.Json(new { currentPassword = current, newPassword = next }));

    private static async Task SetMustChange(MonitoringTestHost host, string username)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CommissionerMonitoringDbContext>();
        (await db.Users.SingleAsync(x => x.LoginName == username)).MustChangePassword = true;
        await db.SaveChangesAsync();
    }

    [Theory]
    [InlineData("commissioner")]
    [InlineData("rolemanager")]
    public async Task AnAdministratorCanChangeItsOwnPasswordAndSignInWithTheNewOne(string username)
    {
        await using var host = await MonitoringTestHost.Create();
        await host.Login(username);

        using var change = await Change(host.Client, MonitoringTestHost.SeedPassword, Fresh);

        Assert.True(change.IsSuccessStatusCode, await change.Content.ReadAsStringAsync());
        using var other = host.NewClient();
        using var old = await MonitoringTestHost.Login(other, username);
        Assert.Equal(HttpStatusCode.Unauthorized, old.StatusCode);
        using var fresh = await MonitoringTestHost.Login(other, username, Fresh);
        Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
    }

    [Theory]
    [InlineData("commissioner")]
    [InlineData("rolemanager")]
    public async Task AnAdministratorIsRefusedOnAWrongCurrentPasswordLikeEveryOtherRole(string username)
    {
        await using var host = await MonitoringTestHost.Create();
        await host.Login(username);

        using var response = await Change(host.Client, "not-the-password", Fresh);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var stillWorks = await host.Login(username);
        Assert.Equal(HttpStatusCode.OK, stillWorks.StatusCode);
    }

    [Theory]
    [InlineData("commissioner")]
    [InlineData("rolemanager")]
    public async Task AnAdministratorKeepsTheMinimumLength(string username)
    {
        await using var host = await MonitoringTestHost.Create();
        await host.Login(username);
        using var response = await Change(host.Client, MonitoringTestHost.SeedPassword, "short");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Theory]
    [InlineData("commissioner")]
    [InlineData("rolemanager")]
    public async Task AnAdministratorWithATemporaryPasswordIsNotLockedOut(string username)
    {
        await using var host = await MonitoringTestHost.Create();
        await SetMustChange(host, username);
        await host.Login(username);

        var session = await host.Client.GetFromJsonAsync(Root + "/session");
        Assert.True(session.GetProperty("mustChangePassword").GetBoolean());
        using var held = await host.Client.GetAsync(Root + "/services");
        Assert.Equal(HttpStatusCode.Forbidden, held.StatusCode);
        Assert.Contains("password_change_required", await held.Content.ReadAsStringAsync());

        using var change = await Change(host.Client, MonitoringTestHost.SeedPassword, Fresh);
        Assert.True(change.IsSuccessStatusCode, await change.Content.ReadAsStringAsync());
        await host.Login(username, Fresh);
        session = await host.Client.GetFromJsonAsync(Root + "/session");
        Assert.False(session.GetProperty("mustChangePassword").GetBoolean());
    }

    [Theory]
    [InlineData("commissioner")]
    [InlineData("rolemanager")]
    [InlineData("anchal1")]
    public async Task EverySessionReportsMustChangePasswordFalseWhenNoneIsPending(string username)
    {
        await using var host = await MonitoringTestHost.Create();
        await host.Login(username);
        var session = await host.Client.GetFromJsonAsync(Root + "/session");
        Assert.False(session.GetProperty("mustChangePassword").GetBoolean());
    }

    [Fact]
    public async Task ChangingOwnPasswordNeverTouchesAnotherAccount()
    {
        await using var host = await MonitoringTestHost.Create();
        await host.Login("rolemanager");
        using var change = await Change(host.Client, MonitoringTestHost.SeedPassword, Fresh);
        Assert.True(change.IsSuccessStatusCode);
        using var other = host.NewClient();
        foreach (var name in new[] { "commissioner", "anchal1" })
        {
            using var response = await MonitoringTestHost.Login(other, name);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}

internal static class JsonClientExtensions
{
    public static Task<JsonElement> GetFromJsonAsync(this HttpClient http, string url) =>
        System.Net.Http.Json.HttpClientJsonExtensions.GetFromJsonAsync<JsonElement>(http, url);
}

/// The service master is guarded by a list of who may read it. The list is pinned here so that
/// widening it is a decision someone has to make in a test, not something a new role inherits.
public sealed class ServiceMasterAllowListTests
{
    [Fact]
    public void OnlyTheAccountsThatFileAndReportMayReadTheServiceMaster() =>
        Assert.Equal(["commissioner", "office", "officer"],
            Anchal.Application.CommissionerMonitoring.Services.MonitoringService.ServiceMasterReaders.Order().ToArray());

    [Theory]
    [InlineData("commissioner", HttpStatusCode.OK)]
    [InlineData("anchal1", HttpStatusCode.OK)]
    [InlineData("rolemanager", HttpStatusCode.Forbidden)]
    public async Task EachSessionRoleIsAdmittedOrRefusedByTheList(string username, HttpStatusCode expected)
    {
        await using var host = await MonitoringTestHost.Create();
        await host.Login(username);
        using var response = await host.Client.GetAsync(MonitoringTestHost.Root + "/services");
        Assert.Equal(expected, response.StatusCode);
    }
}
