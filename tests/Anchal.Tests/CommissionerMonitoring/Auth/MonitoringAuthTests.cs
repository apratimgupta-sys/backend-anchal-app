using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Xunit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Anchal.Application.CommissionerMonitoring.Auth;
using Anchal.Infrastructure.CommissionerMonitoring.Auth;
using Anchal.Infrastructure.CommissionerMonitoring.Persistence;
using Anchal.Infrastructure.CommissionerMonitoring.Seeding;

namespace Anchal.Tests.CommissionerMonitoring.Auth;

public sealed class MonitoringAuthTests
{
    private const string Password = "MonitoringDemoPass12!";
    private const string Secret = "monitoring-auth-test-secret-at-least-thirty-two-bytes-long";

    [Fact]
    public async Task OfficeTokenCanAccessOnlyItsAssignedOffice()
    {
        await using var provider = await ProvisionedServices();
        using var scope = provider.CreateScope();
        var account = scope.ServiceProvider.GetRequiredService<IMonitoringAuthenticator>();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var db = scope.ServiceProvider.GetRequiredService<CommissionerMonitoringDbContext>();
        var token = await account.SignInAsync("anchal1", Password);
        var principal = (await AuthenticateAsync(provider, token!)).Principal!;
        var ownOffice = await db.Offices.SingleAsync(x => x.Code == "anchal1");
        var otherOffice = await db.Offices.SingleAsync(x => x.Code == "anchal2");

        Assert.True((await auth.AuthorizeAsync(principal, MonitoringPolicies.Office)).Succeeded);
        Assert.True((await auth.AuthorizeAsync(principal, ownOffice.Id, MonitoringPolicies.OfficeScope)).Succeeded);
        Assert.False((await auth.AuthorizeAsync(principal, otherOffice.Id, MonitoringPolicies.OfficeScope)).Succeeded);
        Assert.False((await auth.AuthorizeAsync(principal, MonitoringPolicies.Commissioner)).Succeeded);
    }

    [Fact]
    public async Task CommissionerCanUseOnlyCommissionerPolicy()
    {
        await using var provider = await ProvisionedServices();
        using var scope = provider.CreateScope();
        var account = scope.ServiceProvider.GetRequiredService<IMonitoringAuthenticator>();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var principal = (await AuthenticateAsync(provider,
            (await account.SignInAsync("commissioner", Password))!)).Principal!;

        Assert.True((await auth.AuthorizeAsync(principal, MonitoringPolicies.Commissioner)).Succeeded);
        Assert.False((await auth.AuthorizeAsync(principal, MonitoringPolicies.Office)).Succeeded);
    }

    [Fact]
    public async Task ExistingApnaRanchiClaimsCannotUseMonitoringPolicies()
    {
        await using var provider = await ProvisionedServices();
        using var scope = provider.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(ClaimTypes.Role, "Admin"),
             new Claim("Permission", "REPORT_VIEW")], "Bearer"));

        Assert.False((await auth.AuthorizeAsync(principal, MonitoringPolicies.Office)).Succeeded);
        Assert.False((await auth.AuthorizeAsync(principal, MonitoringPolicies.Commissioner)).Succeeded);
    }

    [Fact]
    public async Task MissingOrRevokedOfficeAssignmentDeniesAccess()
    {
        await using var provider = await ProvisionedServices();
        using var scope = provider.CreateScope();
        var account = scope.ServiceProvider.GetRequiredService<IMonitoringAuthenticator>();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var db = scope.ServiceProvider.GetRequiredService<CommissionerMonitoringDbContext>();
        var principal = (await AuthenticateAsync(provider,
            (await account.SignInAsync("anchal1", Password))!)).Principal!;
        var assignment = await db.UserOffices.Include(x => x.User)
            .SingleAsync(x => x.User.LoginName == "anchal1");
        db.UserOffices.Remove(assignment);
        await db.SaveChangesAsync();

        Assert.False((await auth.AuthorizeAsync(principal, MonitoringPolicies.Office)).Succeeded);
    }

    [Fact]
    public async Task InvalidCredentialsOrTamperedTokenAreRejected()
    {
        await using var provider = await ProvisionedServices();
        using var scope = provider.CreateScope();
        var account = scope.ServiceProvider.GetRequiredService<IMonitoringAuthenticator>();
        Assert.Null(await account.SignInAsync("anchal1", "incorrect-password"));
        Assert.Null(await account.SignInAsync("unknown", Password));
        var token = (await account.SignInAsync("anchal1", Password))!;
        var segments = token.Split('.');
        segments[2] = segments[2][0] == 'a' ? "b" + segments[2][1..] : "a" + segments[2][1..];
        Assert.False((await AuthenticateAsync(provider, string.Join('.', segments))).Succeeded);
    }

    [Fact]
    public async Task ForeignIssuerOrAudienceTokenCannotAuthorizeAnchal()
    {
        await using var provider = Services();
        var claims = new[] { new Claim(MonitoringPolicies.ScopeClaim, MonitoringPolicies.ScopeValue) };
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)), SecurityAlgorithms.HmacSha256);

        foreach (var (issuer, audience) in new[] {
            ("foreign-issuer", "anchal-client"), ("anchal-api", "foreign-audience") })
        {
            var token = new JwtSecurityToken(issuer, audience, claims,
                expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: credentials);
            Assert.False((await AuthenticateAsync(provider,
                new JwtSecurityTokenHandler().WriteToken(token))).Succeeded);
        }
    }

    [Fact]
    public async Task ProvisioningIsIdempotentAndPreservesExistingPasswords()
    {
        await using var provider = Services();
        using var scope = provider.CreateScope();
        var provisioner = scope.ServiceProvider.GetRequiredService<IMonitoringProvisioner>();
        var db = scope.ServiceProvider.GetRequiredService<CommissionerMonitoringDbContext>();
        await provisioner.ProvisionAsync(Credentials());
        var firstHash = (await db.Users.SingleAsync(x => x.LoginName == "anchal1")).PasswordHash;
        await provisioner.ProvisionAsync(Credentials("DifferentSecurePass12!"));

        Assert.Equal(7, await db.Users.CountAsync());
        Assert.Equal(6, await db.Offices.CountAsync());
        Assert.Equal(6, await db.UserOffices.CountAsync());
        Assert.Equal(25, await db.CatalogItems.CountAsync());
        Assert.Equal(firstHash, (await db.Users.SingleAsync(x => x.LoginName == "anchal1")).PasswordHash);
        Assert.Equal("नए होल्डिंग प्राप्त करने हेतु आवेदन",
            (await db.CatalogItems.SingleAsync(x => x.Key == "1")).Name);
        Assert.Equal("Community Hall Booking",
            (await db.CatalogItems.SingleAsync(x => x.Key == "25")).Name);
    }

    [Fact]
    public async Task ProvisioningRejectsConflictingExistingRoleAndOfficeWithoutAddingAccounts()
    {
        await using var provider = Services();
        using var scope = provider.CreateScope();
        var provisioner = scope.ServiceProvider.GetRequiredService<IMonitoringProvisioner>();
        var db = scope.ServiceProvider.GetRequiredService<CommissionerMonitoringDbContext>();
        await provisioner.ProvisionAsync(Credentials());

        var officeUser = await db.Users.SingleAsync(x => x.LoginName == "anchal1");
        officeUser.Role = MonitoringPolicies.CommissionerRole;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => provisioner.ProvisionAsync(Credentials()));
        Assert.Equal(7, await db.Users.CountAsync());
        Assert.Equal(25, await db.CatalogItems.CountAsync());

        officeUser.Role = MonitoringPolicies.OfficeRole;
        var assignment = await db.UserOffices.SingleAsync(x => x.UserId == officeUser.Id);
        var otherOffice = await db.Offices.SingleAsync(x => x.Code == "anchal2");
        db.UserOffices.Remove(assignment);
        await db.SaveChangesAsync();
        db.UserOffices.Add(new Anchal.Domain.CommissionerMonitoring.MonitoringUserOffice
        {
            UserId = officeUser.Id, OfficeId = otherOffice.Id, AssignedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => provisioner.ProvisionAsync(Credentials()));
        Assert.Equal(otherOffice.Id, (await db.UserOffices.SingleAsync(x => x.UserId == officeUser.Id)).OfficeId);
    }

    private static async Task<ServiceProvider> ProvisionedServices()
    {
        var provider = Services();
        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IMonitoringProvisioner>().ProvisionAsync(Credentials());
        return provider;
    }

    private static ServiceProvider Services()
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
        return services.BuildServiceProvider();
    }

    private static Dictionary<string, string> Credentials(string password = Password) =>
        new[] { "anchal1", "anchal2", "anchal3", "anchal4", "anchal5", "anchal6", "commissioner" }
            .ToDictionary(x => x, _ => password);

    private static Task<AuthenticateResult> AuthenticateAsync(ServiceProvider provider, string token)
    {
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Request.Headers.Authorization = $"Bearer {token}";
        return provider.GetRequiredService<IAuthenticationService>()
            .AuthenticateAsync(context, MonitoringPolicies.AuthenticationScheme);
    }

}
