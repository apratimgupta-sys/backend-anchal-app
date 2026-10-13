using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Anchal.Application.CommissionerMonitoring.Auth;
using Anchal.Infrastructure.CommissionerMonitoring.Seeding;

namespace Anchal.Infrastructure.CommissionerMonitoring.Auth;

public static class MonitoringAuthenticationRegistration
{
    public static IServiceCollection AddMonitoringAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication()
            .AddJwtBearer(MonitoringPolicies.AuthenticationScheme, _ => { });
        services.AddOptions<JwtBearerOptions>(MonitoringPolicies.AuthenticationScheme)
            .Configure<IConfiguration>((options, configuration) =>
            {
                var secret = Required(configuration, "Secret");
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                    ValidateIssuer = true,
                    ValidIssuer = Required(configuration, "Issuer"),
                    ValidateAudience = true,
                    ValidAudience = Required(configuration, "Audience"),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };
            });
        services.AddScoped<IAuthorizationHandler, MonitoringAuthorizationHandler>();
        services.AddScoped<IMonitoringAuthenticator, MonitoringAuthenticator>();
        services.AddScoped<IMonitoringProvisioner, MonitoringProvisioner>();
        services.AddScoped<MonitoringPasswordHasher>();
        return services;
    }

    private static string Required(IConfiguration configuration, string key) =>
        configuration[$"Anchal:Jwt:{key}"] is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Anchal:Jwt:{key} is required.");
}
