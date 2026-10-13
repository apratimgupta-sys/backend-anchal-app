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
    public static void ValidateConfiguration(IConfiguration configuration)
    {
        var secret = Required(configuration, "Secret");
        if (Encoding.UTF8.GetByteCount(secret) < 32)
            throw new InvalidOperationException("Anchal:Jwt:Secret must be at least 32 bytes.");
        _ = Required(configuration, "Issuer");
        _ = Required(configuration, "Audience");
        if (!int.TryParse(configuration["Anchal:Jwt:ExpiryMinutes"], out var minutes) || minutes is < 1 or > 1440)
            throw new InvalidOperationException("Anchal:Jwt:ExpiryMinutes must be between 1 and 1440.");
    }

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

    internal static string Required(IConfiguration configuration, string key) =>
        configuration[$"Anchal:Jwt:{key}"] is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Anchal:Jwt:{key} is required.");
}
