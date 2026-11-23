using System.Security.Claims;
using System.Text;
using Xunit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Anchal.Application.CommissionerMonitoring.Auth;
using Anchal.Domain.CommissionerMonitoring;
using Anchal.Infrastructure.CommissionerMonitoring.Auth;
using Anchal.Infrastructure.CommissionerMonitoring.Persistence;

namespace Anchal.Tests.CommissionerMonitoring.Auth;

// The role manager administers users and nothing else. Until the officer endpoints move to it,
// a role-manager token must be denied everywhere except its own policy.
public sealed class RoleManagerAuthorizationTests
{
    private const string Password = "MonitoringDemoPass12!";
    private const string Secret = "monitoring-auth-test-secret-at-least-thirty-two-bytes-long";
    private const string LoginName = "rolemanager";

    [Fact]
    public async Task RoleManagerSatisfiesTheRoleManagerPolicy()
    {
        await using var provider = await ProvisionedServices();
        using var scope = provider.CreateScope();
        var principal = await SignInPrincipal(provider, scope);

        Assert.True((await Auth(scope).AuthorizeAsync(principal, MonitoringPolicies.RoleManager)).Succeeded);
    }

    [Fact]
    public async Task RoleManagerSatisfiesNoOtherMonitoringPolicy()
    {
        await using var provider = await ProvisionedServices();
        using var scope = provider.CreateScope();
        var principal = await SignInPrincipal(provider, scope);
        var db = scope.ServiceProvider.GetRequiredService<CommissionerMonitoringDbContext>();
        var office = await db.Offices.FirstAsync();

        Assert.False((await Auth(scope).AuthorizeAsync(principal, MonitoringPolicies.Office)).Succeeded);
        Assert.False((await Auth(scope).AuthorizeAsync(principal, MonitoringPolicies.Commissioner)).Succeeded);
        Assert.False((await Auth(scope).AuthorizeAsync(principal, MonitoringPolicies.FieldOfficer)).Succeeded);
        Assert.False((await Auth(scope).AuthorizeAsync(principal, office.Id, MonitoringPolicies.OfficeScope)).Succeeded);
    }

    [Fact]
    public async Task OtherRolesAreStillDeniedTheRoleManagerPolicy()
    {
        await using var provider = await ProvisionedServices();
        using var scope = provider.CreateScope();

        foreach (var login in new[] { "anchal1", "commissioner" })
        {
            var principal = await SignInPrincipal(provider, scope, login);
            Assert.False((await Auth(scope).AuthorizeAsync(principal, MonitoringPolicies.RoleManager)).Succeeded);
        }
    }

    [Fact]
    public async Task RoleManagerCarryingAnOfficeAssignmentIsDenied()
    {
        await using var provider = await ProvisionedServices();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CommissionerMonitoringDbContext>();
        var principal = await SignInPrincipal(provider, scope);
        Assert.True((await Auth(scope).AuthorizeAsync(principal, MonitoringPolicies.RoleManager)).Succeeded);

        var user = await db.Users.SingleAsync(x => x.LoginName == LoginName);
        var office = await db.Offices.FirstAsync();
        db.UserOffices.Add(new MonitoringUserOffice
        {
            UserId = user.Id, OfficeId = office.Id, AssignedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        Assert.False((await Auth(scope).AuthorizeAsync(principal, MonitoringPolicies.RoleManager)).Succeeded);
    }

    [Fact]
    public async Task RoleManagerTokenCarryingAnOfficeClaimIsDenied()
    {
        await using var provider = await ProvisionedServices();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CommissionerMonitoringDbContext>();
        var principal = await SignInPrincipal(provider, scope);
        var office = await db.Offices.FirstAsync();
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(MonitoringPolicies.OfficeIdClaim, office.Id.ToString()));

        Assert.False((await Auth(scope).AuthorizeAsync(principal, MonitoringPolicies.RoleManager)).Succeeded);
    }

    [Fact]
    public async Task UnknownRoleStringSatisfiesNothing()
    {
        await using var provider = await ProvisionedServices();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CommissionerMonitoringDbContext>();
        var user = await db.Users.SingleAsync(x => x.LoginName == LoginName);
        user.Role = "Auditor";
        await db.SaveChangesAsync();
        var principal = Forge(user.Id, "Auditor");
        var office = await db.Offices.FirstAsync();

        Assert.False((await Auth(scope).AuthorizeAsync(principal, MonitoringPolicies.RoleManager)).Succeeded);
        Assert.False((await Auth(scope).AuthorizeAsync(principal, MonitoringPolicies.Office)).Succeeded);
        Assert.False((await Auth(scope).AuthorizeAsync(principal, MonitoringPolicies.Commissioner)).Succeeded);
        Assert.False((await Auth(scope).AuthorizeAsync(principal, MonitoringPolicies.FieldOfficer)).Succeeded);
        Assert.False((await Auth(scope).AuthorizeAsync(principal, office.Id, MonitoringPolicies.OfficeScope)).Succeeded);
    }

    [Fact]
    public async Task ProvisionerCreatesTheRoleManagerWithNoOfficeAssignment()
    {
        await using var provider = await ProvisionedServices();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CommissionerMonitoringDbContext>();

        var user = await db.Users.Include(x => x.OfficeAssignments).SingleAsync(x => x.LoginName == LoginName);
        Assert.Equal(MonitoringPolicies.RoleManagerRole, user.Role);
        Assert.Empty(user.OfficeAssignments);
        Assert.True(user.IsActive);
        Assert.Equal("Role Manager", user.DisplayName);
        Assert.Equal(6, await db.UserOffices.CountAsync());
    }

    [Fact]
    public async Task SignInRefusesARoleManagerThatHasAnOfficeAssignment()
    {
        await using var provider = await ProvisionedServices();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CommissionerMonitoringDbContext>();
        var account = scope.ServiceProvider.GetRequiredService<IMonitoringAuthenticator>();
        var user = await db.Users.SingleAsync(x => x.LoginName == LoginName);
        db.UserOffices.Add(new MonitoringUserOffice
        {
            UserId = user.Id, OfficeId = (await db.Offices.FirstAsync()).Id, AssignedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        Assert.Null(await account.SignInAsync(LoginName, Password));
    }

    private static IAuthorizationService Auth(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IAuthorizationService>();

    private static async Task<ClaimsPrincipal> SignInPrincipal(
        ServiceProvider provider, IServiceScope scope, string login = LoginName)
    {
        var account = scope.ServiceProvider.GetRequiredService<IMonitoringAuthenticator>();
        var token = await account.SignInAsync(login, Password);
        Assert.NotNull(token);
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Request.Headers.Authorization = $"Bearer {token}";
        var result = await provider.GetRequiredService<IAuthenticationService>()
            .AuthenticateAsync(context, MonitoringPolicies.AuthenticationScheme);
        Assert.True(result.Succeeded);
        return result.Principal!;
    }

    private static ClaimsPrincipal Forge(Guid userId, string role) => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
        new Claim(MonitoringPolicies.UserIdClaim, userId.ToString()),
        new Claim(MonitoringPolicies.ScopeClaim, MonitoringPolicies.ScopeValue),
        new Claim(MonitoringPolicies.RoleClaim, role)
    ], "Bearer"));

    private static async Task<ServiceProvider> ProvisionedServices()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Anchal:Jwt:Secret"] = Secret,
            ["Anchal:Jwt:Issuer"] = "anchal-api",
            ["Anchal:Jwt:Audience"] = "anchal-client",
            ["Anchal:Jwt:ExpiryMinutes"] = "60"
        }).Build();
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddDbContext<CommissionerMonitoringDbContext>(x => x.UseInMemoryDatabase(databaseName));
        services.AddMonitoringAuthentication();
        services.AddAuthorization(options => MonitoringPolicies.AddTo(options));
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var credentials = new[] { "anchal1", "anchal2", "anchal3", "anchal4", "anchal5", "anchal6", "commissioner", LoginName }
            .ToDictionary(x => x, _ => Password);
        await scope.ServiceProvider.GetRequiredService<IMonitoringProvisioner>().ProvisionAsync(credentials);
        return provider;
    }
}
