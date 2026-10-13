using Anchal.Api.CommissionerMonitoring;
using Anchal.Api.Provisioning;
using Anchal.Application.CommissionerMonitoring.Auth;
using Anchal.Infrastructure.CommissionerMonitoring;
using Anchal.Infrastructure.CommissionerMonitoring.Auth;
using Anchal.Infrastructure.CommissionerMonitoring.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var app = AnchalApplication.Build(args);
if (args.Length > 0 && args[0] == "provision")
{
    CommissionerMonitoringConnectionValidator.Validate(app.Configuration, app.Environment.EnvironmentName);
    MonitoringAuthenticationRegistration.ValidateConfiguration(app.Configuration);
    if (args.Length != 3 || args[1] != "--credentials-file")
        throw new InvalidOperationException("Usage: provision --credentials-file /protected/anchal-provisioning.env");
    await using var scope = app.Services.CreateAsyncScope();
    var credentials = AnchalProvisioningCredentials.Read(args[2]);
    await scope.ServiceProvider.GetRequiredService<IMonitoringProvisioner>().ProvisionAsync(credentials);
    Console.WriteLine("Anchal provisioning completed.");
    return;
}
app.Run();

public partial class Program { }

public static class AnchalApplication
{
    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        configure?.Invoke(builder);
        builder.Services.AddCommissionerMonitoringPersistence(builder.Configuration, builder.Environment);
        builder.Services.AddCommissionerMonitoringWorkflows();
        builder.Services.AddMonitoringAuthentication();
        builder.Services.AddAuthorization(MonitoringPolicies.AddTo);
        builder.Services.AddControllers();
        builder.Services.AddCommissionerMonitoringApi(builder.Configuration);
        builder.Services.AddHealthChecks()
            .AddCheck<CommissionerMonitoringDatabaseHealthCheck>("commissioner-monitoring");

        CommissionerMonitoringConnectionValidator.Validate(builder.Configuration, builder.Environment.EnvironmentName);
        MonitoringAuthenticationRegistration.ValidateConfiguration(builder.Configuration);

        var app = builder.Build();
        app.UseForwardedHeaders(AnchalForwardedHeaders.CreateOptions());
        app.UseCommissionerMonitoringApi();
        app.UseWhen(context => !context.Request.Path.StartsWithSegments("/health"), monitoring =>
        {
            monitoring.UseCors(CommissionerMonitoringApiRegistration.CorsPolicy);
            monitoring.UseRateLimiter();
            monitoring.UseAuthentication();
            monitoring.UseAuthorization();
        });
        app.MapControllers();
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            Predicate = _ => false
        });
        app.MapHealthChecks("/health/commissioner-monitoring", new HealthCheckOptions
        {
            Predicate = check => check.Name == "commissioner-monitoring",
            ResponseWriter = async (context, report) =>
            {
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new
                {
                    status = report.Status == HealthStatus.Healthy ? "healthy" : "unhealthy",
                    service = "anchal"
                });
            }
        });
        return app;
    }

}

public sealed class CommissionerMonitoringDatabaseHealthCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<CommissionerMonitoringDbContext>();
            return await database.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy();
        }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy();
        }
    }
}
