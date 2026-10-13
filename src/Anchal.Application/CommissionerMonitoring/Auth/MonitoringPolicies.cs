using Microsoft.AspNetCore.Authorization;

namespace Anchal.Application.CommissionerMonitoring.Auth;

public static class MonitoringPolicies
{
    public const string AuthenticationScheme = "CommissionerMonitoringJwt";
    public const string Office = "MonitoringOffice";
    public const string Commissioner = "MonitoringCommissioner";
    public const string OfficeScope = "MonitoringOfficeScope";
    public const string ScopeClaim = "monitoring_scope";
    public const string ScopeValue = "commissioner_monitoring";
    public const string RoleClaim = "monitoring_role";
    public const string UserIdClaim = "monitoring_user_id";
    public const string OfficeIdClaim = "monitoring_office_id";
    public const string OfficeRole = "AnchalOffice";
    public const string CommissionerRole = "Commissioner";

    public static void AddTo(AuthorizationOptions options)
    {
        options.AddPolicy(Office, policy => policy.AddAuthenticationSchemes(AuthenticationScheme).RequireAuthenticatedUser()
            .AddRequirements(new MonitoringRoleRequirement(OfficeRole)));
        options.AddPolicy(Commissioner, policy => policy.AddAuthenticationSchemes(AuthenticationScheme).RequireAuthenticatedUser()
            .AddRequirements(new MonitoringRoleRequirement(CommissionerRole)));
        options.AddPolicy(OfficeScope, policy => policy.AddAuthenticationSchemes(AuthenticationScheme).RequireAuthenticatedUser()
            .AddRequirements(new MonitoringOfficeScopeRequirement()));
    }
}

public sealed record MonitoringRoleRequirement(string Role) : IAuthorizationRequirement;
public sealed record MonitoringOfficeScopeRequirement : IAuthorizationRequirement;

public interface IMonitoringAuthenticator
{
    Task<string?> SignInAsync(string loginName, string password, CancellationToken cancellationToken = default);
}

public interface IMonitoringProvisioner
{
    Task ProvisionAsync(IReadOnlyDictionary<string, string> credentials, CancellationToken cancellationToken = default);
}
