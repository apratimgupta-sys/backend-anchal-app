using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Anchal.Application.CommissionerMonitoring.Auth;
using Anchal.Infrastructure.CommissionerMonitoring.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Anchal.Tests.CommissionerMonitoring.FieldVisits;

/// The role manager owns user administration; the commissioner does not. A role manager may
/// create field officers and extra logins for the six existing Anchals, and nothing else.
public sealed class RoleManagerUserAdministrationTests
{
    private const string Root = MonitoringTestHost.Root;
    private static readonly Guid SomeId = Guid.NewGuid();

    private static object Body(string username = "new.user", string? role = "FieldOfficer", int? homeOfficeId = 3) =>
        new Dictionary<string, object?>
        {
            ["username"] = username, ["name"] = "A. Kumar", ["designation"] = "Junior Engineer",
            ["role"] = role, ["homeOfficeId"] = homeOfficeId
        };

    private static Task<HttpResponseMessage> Post(MonitoringTestHost host, object body) =>
        host.Client.PostAsync(Root + "/officers", MonitoringTestHost.Json(body));

    private static async Task<int> UserCount(MonitoringTestHost host)
    {
        using var scope = host.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CommissionerMonitoringDbContext>().Users.CountAsync();
    }

    // 1
    [Fact]
    public async Task ACommissionerMayListOfficersButIsRefusedEveryWrite()
    {
        await using var host = await MonitoringTestHost.Create();
        await host.Login("rolemanager");
        using var created = await Post(host, Body("target.officer"));
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("officer").GetProperty("id").GetString();
        var before = await UserCount(host);
        await host.Login("commissioner");

        using var list = await host.Client.GetAsync(Root + "/officers");
        using var create = await Post(host, Body("by.commissioner"));
        using var setActive = await host.Client.PatchAsync(Root + "/officers/" + id,
            MonitoringTestHost.Json(new { active = false }));
        using var reset = await host.Client.PostAsync(Root + $"/officers/{id}/password-reset",
            MonitoringTestHost.Json(new { }));

        // Reading who the officers are feeds the visit-register filter; it is not administration.
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var visible = await list.Content.ReadFromJsonAsync<JsonElement[]>();
        Assert.Equal(["target.officer"], visible!.Select(x => x.GetProperty("username").GetString()).ToArray());
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, setActive.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, reset.StatusCode);
        Assert.Equal(before, await UserCount(host));
        await host.Login("rolemanager");
        var roster = await host.Client.GetFromJsonAsync<JsonElement[]>(Root + "/officers");
        Assert.True(roster!.Single(x => x.GetProperty("username").GetString() == "target.officer").GetProperty("active").GetBoolean());
    }

    [Fact]
    public async Task AnOfficeLoginCannotReadTheRoster()
    {
        await using var host = await MonitoringTestHost.Create();
        await host.Login("anchal1");
        using var office = await host.Client.GetAsync(Root + "/officers");
        Assert.Equal(HttpStatusCode.Forbidden, office.StatusCode);
    }

    [Fact]
    public async Task AnOfficerCannotReadTheRosterEither()
    {
        await using var host = await MonitoringTestHost.Create();
        await host.SignInAsOfficer();
        using var response = await host.Client.GetAsync(Root + "/officers");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TheSessionOfAnOfficeLoginCarriesMustChangePasswordFromTheRow()
    {
        await using var host = await MonitoringTestHost.Create();
        await host.Login("rolemanager");
        using var created = await Post(host, Body("anchal6.second", "AnchalOffice", 6));
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var temporary = body.GetProperty("temporaryPassword").GetString()!;
        var id = body.GetProperty("officer").GetProperty("id").GetString();

        // A seeded office login has no pending change.
        using var seeded = host.NewClient();
        await MonitoringTestHost.Login(seeded, "anchal6");
        Assert.False((await seeded.GetFromJsonAsync<JsonElement>(Root + "/session")).GetProperty("mustChangePassword").GetBoolean());

        // A created one does, and the session says so.
        using var second = host.NewClient();
        await MonitoringTestHost.Login(second, "anchal6.second", temporary);
        Assert.True((await second.GetFromJsonAsync<JsonElement>(Root + "/session")).GetProperty("mustChangePassword").GetBoolean());
        var changed = "Second#2026ab";
        using var change = await second.PostAsync(Root + "/password",
            MonitoringTestHost.Json(new { currentPassword = temporary, newPassword = changed }));
        Assert.True(change.IsSuccessStatusCode);
        await MonitoringTestHost.Login(second, "anchal6.second", changed);
        Assert.False((await second.GetFromJsonAsync<JsonElement>(Root + "/session")).GetProperty("mustChangePassword").GetBoolean());
        using var fine = await second.GetAsync(Root + "/office/setup");
        Assert.Equal(HttpStatusCode.OK, fine.StatusCode);

        // The role manager resets it while that login is still signed in. The token's claim is stale
        // (it was minted without the flag); the session reads the row, so it reports true at once and
        // other routes are held with the same code a fresh login gets.
        using var reset = await host.Client.PostAsync(Root + $"/officers/{id}/password-reset",
            MonitoringTestHost.Json(new { }));
        Assert.True(reset.IsSuccessStatusCode);
        Assert.True((await second.GetFromJsonAsync<JsonElement>(Root + "/session")).GetProperty("mustChangePassword").GetBoolean());
        using var held = await second.GetAsync(Root + "/office/setup");
        Assert.Equal(HttpStatusCode.Forbidden, held.StatusCode);
        Assert.Contains("password_change_required", await held.Content.ReadAsStringAsync());
    }

    // 2
    [Fact]
    public async Task ARoleManagerIsAllowedEveryOfficerEndpoint()
    {
        await using var host = await MonitoringTestHost.Create();
        await host.Login("rolemanager");
        using var create = await Post(host, Body());
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("officer").GetProperty("id").GetString();

        using var list = await host.Client.GetAsync(Root + "/officers");
        using var setActive = await host.Client.PatchAsync(Root + "/officers/" + id,
            MonitoringTestHost.Json(new { active = false }));
        using var reset = await host.Client.PostAsync(Root + $"/officers/{id}/password-reset",
            MonitoringTestHost.Json(new { }));

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.OK, setActive.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
    }

    // 3
    [Fact]
    public async Task ACreatedFieldOfficerMatchesOneBuiltTheOldWay()
    {
        await using var host = await MonitoringTestHost.Create();
        await host.Login("rolemanager");
        using var response = await Post(host, Body("je.three", "FieldOfficer", 3));
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CommissionerMonitoringDbContext>();
        var user = await db.Users.Include(x => x.OfficeAssignments).ThenInclude(x => x.Office)
            .SingleAsync(x => x.LoginName == "je.three");
        Assert.Equal(MonitoringPolicies.FieldOfficerRole, user.Role);
        Assert.Equal("anchal3", user.OfficeAssignments.Single().Office.Code);
        Assert.True(user.MustChangePassword);

        var principal = await Principal(host, scope, "je.three", created.GetProperty("temporaryPassword").GetString()!);
        var auth = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var own = user.OfficeAssignments.Single().OfficeId;
        var other = (await db.Offices.FirstAsync(x => x.Id != own)).Id;
        Assert.True((await auth.AuthorizeAsync(principal, MonitoringPolicies.FieldOfficer)).Succeeded);
        Assert.False((await auth.AuthorizeAsync(principal, MonitoringPolicies.Office)).Succeeded);
        Assert.False((await auth.AuthorizeAsync(principal, MonitoringPolicies.RoleManager)).Succeeded);
        // A field officer never satisfies OfficeScope, for its own office or another: that policy is
        // the office account's. The created officer must behave exactly like that.
        Assert.False((await auth.AuthorizeAsync(principal, own, MonitoringPolicies.OfficeScope)).Succeeded);
        Assert.False((await auth.AuthorizeAsync(principal, other, MonitoringPolicies.OfficeScope)).Succeeded);
    }

    // 4
    [Fact]
    public async Task ACreatedOfficeLoginIsScopedToItsOwnAnchalOnly()
    {
        await using var host = await MonitoringTestHost.Create();
        await host.Login("rolemanager");
        using var response = await Post(host, Body("anchal2.second", "AnchalOffice", 2));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CommissionerMonitoringDbContext>();
        var user = await db.Users.Include(x => x.OfficeAssignments).ThenInclude(x => x.Office)
            .SingleAsync(x => x.LoginName == "anchal2.second");
        Assert.Equal(MonitoringPolicies.OfficeRole, user.Role);
        Assert.Equal("anchal2", user.OfficeAssignments.Single().Office.Code);

        var principal = await Principal(host, scope, "anchal2.second", created.GetProperty("temporaryPassword").GetString()!);
        var auth = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var own = user.OfficeAssignments.Single().OfficeId;
        Assert.True((await auth.AuthorizeAsync(principal, MonitoringPolicies.Office)).Succeeded);
        Assert.True((await auth.AuthorizeAsync(principal, own, MonitoringPolicies.OfficeScope)).Succeeded);
        foreach (var other in await db.Offices.Where(x => x.Id != own).Select(x => x.Id).ToListAsync())
            Assert.False((await auth.AuthorizeAsync(principal, other, MonitoringPolicies.OfficeScope)).Succeeded);
        Assert.False((await auth.AuthorizeAsync(principal, MonitoringPolicies.FieldOfficer)).Succeeded);
        Assert.False((await auth.AuthorizeAsync(principal, MonitoringPolicies.RoleManager)).Succeeded);
    }

    // 5 and 6, posted directly: the UI is not the control.
    [Theory]
    [InlineData("Commissioner")]
    [InlineData("RoleManager")]
    [InlineData("commissioner")]
    [InlineData("rolemanager")]
    [InlineData("ROLEMANAGER")]
    [InlineData("Auditor")]
    [InlineData("")]
    [InlineData(null)]
    public async Task ARoleTheClientDidNotAgreeToIsRejectedAndCreatesNobody(string? role)
    {
        await using var host = await MonitoringTestHost.Create();
        await host.Login("rolemanager");
        var before = await UserCount(host);

        // With and without an office id: neither shape may slip through.
        using var withOffice = await Post(host, Body("wants.power", role, 1));
        using var withoutOffice = await Post(host, Body("wants.power2", role, null));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, withOffice.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, withoutOffice.StatusCode);
        Assert.Contains("\"role\"", await withOffice.Content.ReadAsStringAsync());
        Assert.Equal(before, await UserCount(host));
    }

    [Fact]
    public async Task TheRoleFieldIsRequiredNotDefaulted()
    {
        await using var host = await MonitoringTestHost.Create();
        await host.Login("rolemanager");
        using var response = await Post(host,
            new { username = "no.role", name = "A", designation = "JE", homeOfficeId = 3 });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // 7
    [Theory]
    [InlineData("AnchalOffice", null)]
    [InlineData("AnchalOffice", 0)]
    [InlineData("AnchalOffice", 7)]
    [InlineData("AnchalOffice", 99)]
    [InlineData("AnchalOffice", -1)]
    [InlineData("FieldOfficer", null)]
    [InlineData("FieldOfficer", 7)]
    public async Task AnOfficeBoundRoleNeedsOneOfTheSixExistingAnchals(string role, int? homeOfficeId)
    {
        await using var host = await MonitoringTestHost.Create();
        await host.Login("rolemanager");
        var before = await UserCount(host);

        using var response = await Post(host, Body("bad.office", role, homeOfficeId));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("homeOfficeId", await response.Content.ReadAsStringAsync());
        Assert.Equal(before, await UserCount(host));
    }

    [Fact]
    public async Task NoNewAnchalIsEverCreated()
    {
        await using var host = await MonitoringTestHost.Create();
        await host.Login("rolemanager");
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CommissionerMonitoringDbContext>();
        var offices = await db.Offices.CountAsync();

        using var ok = await Post(host, Body("anchal5.extra", "AnchalOffice", 5));
        using var bad = await Post(host, Body("anchal9.extra", "AnchalOffice", 9));

        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, bad.StatusCode);
        Assert.Equal(6, offices);
        Assert.Equal(offices, await db.Offices.CountAsync());
    }

    // 8: the client's actual requirement.
    [Fact]
    public async Task AnAnchalThatAlreadyHasALoginGetsAnotherOne()
    {
        await using var host = await MonitoringTestHost.Create();
        await host.Login("rolemanager");

        using var response = await Post(host, Body("anchal1.second", "AnchalOffice", 1));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var temporary = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("temporaryPassword").GetString()!;
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CommissionerMonitoringDbContext>();
        var logins = await db.Users.Where(x => x.Role == MonitoringPolicies.OfficeRole &&
            x.OfficeAssignments.Any(a => a.Office.Code == "anchal1")).Select(x => x.LoginName).ToListAsync();
        Assert.Equal(["anchal1", "anchal1.second"], logins.Order().ToArray());

        // Both sign in and both land on the same office.
        using var seeded = host.NewClient();
        using var seededSignIn = await MonitoringTestHost.Login(seeded, "anchal1");
        using var second = host.NewClient();
        using var secondSignIn = await MonitoringTestHost.Login(second, "anchal1.second", temporary);
        Assert.Equal(HttpStatusCode.OK, seededSignIn.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondSignIn.StatusCode);
        var a = await seeded.GetFromJsonAsync<JsonElement>(Root + "/session");
        var b = await second.GetFromJsonAsync<JsonElement>(Root + "/session");
        // The adapter reports an office by its number, not its Guid.
        Assert.Equal(1, a.GetProperty("officeId").GetInt32());
        Assert.Equal(1, b.GetProperty("officeId").GetInt32());
        Assert.Equal("office", b.GetProperty("role").GetString());
    }

    // A created office login must be usable: it holds a temporary password, so it has to be able
    // to set its own. The password route was the field officer's alone.
    [Fact]
    public async Task ACreatedOfficeLoginCanSetItsOwnPasswordAndThenWork()
    {
        await using var host = await MonitoringTestHost.Create();
        await host.Login("rolemanager");
        using var created = await Post(host, Body("anchal4.second", "AnchalOffice", 4));
        var temporary = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("temporaryPassword").GetString()!;
        await host.Login("anchal4.second", temporary);

        using var blocked = await host.Client.GetAsync(Root + "/office/setup");
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Contains("password_change_required", await blocked.Content.ReadAsStringAsync());

        using var change = await host.Client.PostAsync(Root + "/password",
            MonitoringTestHost.Json(new { currentPassword = temporary, newPassword = "Second#2026ab" }));
        Assert.True(change.IsSuccessStatusCode, $"{change.StatusCode}: {await change.Content.ReadAsStringAsync()}");

        using var signIn = await host.Login("anchal4.second", "Second#2026ab");
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        using var works = await host.Client.GetAsync(Root + "/office/setup");
        Assert.Equal(HttpStatusCode.OK, works.StatusCode);
    }

    // 9
    [Fact]
    public async Task TheSessionOfARoleManagerReportsItsRoleAndNoOffice()
    {
        await using var host = await MonitoringTestHost.Create();
        using var signIn = await host.Login("rolemanager");
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);

        using var response = await host.Client.GetAsync(Root + "/session");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var session = (await response.Content.ReadFromJsonAsync<JsonElement>());
        Assert.Equal("rolemanager", session.GetProperty("role").GetString());
        Assert.Equal("rolemanager", session.GetProperty("username").GetString());
        // Like the commissioner's: no office keys at all.
        Assert.False(session.TryGetProperty("officeId", out var officeId) && officeId.ValueKind != JsonValueKind.Null);
        Assert.False(session.TryGetProperty("officeCode", out var officeCode) && officeCode.ValueKind != JsonValueKind.Null);
    }

    [Fact]
    public async Task ARoleManagerWithAnOfficeAssignmentGetsNoSession()
    {
        await using var host = await MonitoringTestHost.Create();
        await host.Login("rolemanager");
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CommissionerMonitoringDbContext>();
            var user = await db.Users.SingleAsync(x => x.LoginName == "rolemanager");
            db.UserOffices.Add(new Anchal.Domain.CommissionerMonitoring.MonitoringUserOffice
            {
                UserId = user.Id, OfficeId = (await db.Offices.FirstAsync()).Id, AssignedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        using var response = await host.Client.GetAsync(Root + "/session");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // The role manager administers people, never data: now that it has a session, every
    // route that only checks "any session" must still refuse it.
    [Theory]
    [InlineData("/services")]
    [InlineData("/history")]
    [InlineData("/dashboard")]
    [InlineData("/dashboard/entries")]
    [InlineData("/dashboard/services")]
    [InlineData("/dashboard/days")]
    [InlineData("/office/setup")]
    [InlineData("/office/entries?mode=day")]
    [InlineData("/field-visits?from=2026-11-01&to=2026-11-23")]
    public async Task ARoleManagerIsDeniedEveryDataRoute(string route)
    {
        await using var host = await MonitoringTestHost.Create();
        await host.Login("rolemanager");

        using var response = await host.Client.GetAsync(Root + route);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ARoleManagerCannotWriteDataEither()
    {
        await using var host = await MonitoringTestHost.Create();
        await host.Login("rolemanager");
        var empty = MonitoringTestHost.Json(new { });

        using var setup = await host.Client.PutAsync(Root + "/office/setup", empty);
        using var entry = await host.Client.PostAsync(Root + "/office/entries", empty);
        using var visit = await host.Client.PostAsync(Root + "/field-visits", empty);
        using var voided = await host.Client.PostAsync(Root + $"/field-visits/{SomeId}/void", empty);

        Assert.Equal(HttpStatusCode.Forbidden, setup.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, entry.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, visit.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, voided.StatusCode);
    }

    // A role manager must not be able to lock out or take over the accounts it may not create.
    [Theory]
    [InlineData("commissioner")]
    [InlineData("rolemanager")]
    public async Task ARoleManagerCannotListDeactivateOrResetTheAccountsItMayNotCreate(string login)
    {
        await using var host = await MonitoringTestHost.Create();
        await host.Login("rolemanager");
        Guid id;
        using (var scope = host.Services.CreateScope())
            id = (await scope.ServiceProvider.GetRequiredService<CommissionerMonitoringDbContext>()
                .Users.SingleAsync(x => x.LoginName == login)).Id;

        var roster = await host.Client.GetFromJsonAsync<JsonElement[]>(Root + "/officers");
        using var setActive = await host.Client.PatchAsync(Root + "/officers/" + id,
            MonitoringTestHost.Json(new { active = false }));
        using var reset = await host.Client.PostAsync(Root + $"/officers/{id}/password-reset",
            MonitoringTestHost.Json(new { }));

        Assert.DoesNotContain(roster!, x => x.GetProperty("username").GetString() == login);
        Assert.Equal(HttpStatusCode.NotFound, setActive.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, reset.StatusCode);
        using var stillWorks = await host.Login(login);
        Assert.Equal(HttpStatusCode.OK, stillWorks.StatusCode);
    }

    [Fact]
    public async Task TheRosterNamesEachUsersRole()
    {
        await using var host = await MonitoringTestHost.Create();
        await host.Login("rolemanager");
        await Post(host, Body("je.roster", "FieldOfficer", 2));
        await Post(host, Body("anchal2.roster", "AnchalOffice", 2));

        var roster = await host.Client.GetFromJsonAsync<JsonElement[]>(Root + "/officers");

        string RoleOf(string name) => roster!.Single(x => x.GetProperty("username").GetString() == name)
            .GetProperty("role").GetString()!;
        Assert.Equal("FieldOfficer", RoleOf("je.roster"));
        Assert.Equal("AnchalOffice", RoleOf("anchal2.roster"));
        Assert.Equal("AnchalOffice", RoleOf("anchal1"));
    }

    private static async Task<ClaimsPrincipal> Principal(
        MonitoringTestHost host, IServiceScope scope, string login, string password)
    {
        var token = await scope.ServiceProvider.GetRequiredService<IMonitoringAuthenticator>().SignInAsync(login, password);
        Assert.NotNull(token);
        var context = new DefaultHttpContext { RequestServices = host.Services };
        context.Request.Headers.Authorization = $"Bearer {token}";
        var result = await host.Services.GetRequiredService<IAuthenticationService>()
            .AuthenticateAsync(context, MonitoringPolicies.AuthenticationScheme);
        Assert.True(result.Succeeded);
        return result.Principal!;
    }
}
