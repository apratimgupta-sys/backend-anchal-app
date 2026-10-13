using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Anchal.Application.CommissionerMonitoring.Auth;
using Anchal.Infrastructure.CommissionerMonitoring.Seeding;

namespace Anchal.Infrastructure.CommissionerMonitoring.Auth;

public static class MonitoringAuthenticationRegistration
{
    public static IServiceCollection AddMonitoringAuthentication(this IServiceCollection services)
    {
        services.AddScoped<IAuthorizationHandler, MonitoringAuthorizationHandler>();
        services.AddScoped<IMonitoringAuthenticator, MonitoringAuthenticator>();
        services.AddScoped<IMonitoringProvisioner, MonitoringProvisioner>();
        services.AddScoped<MonitoringPasswordHasher>();
        return services;
    }
}
