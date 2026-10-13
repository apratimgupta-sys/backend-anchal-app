using Anchal.Infrastructure.CommissionerMonitoring.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Anchal.Infrastructure.CommissionerMonitoring;

public static class CommissionerMonitoringRegistration
{
    public static IServiceCollection AddCommissionerMonitoringPersistence(this IServiceCollection services,
        IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddDbContext<CommissionerMonitoringDbContext>(options =>
        {
            var connection = CommissionerMonitoringConnectionValidator.Validate(configuration, environment.EnvironmentName);
            options.UseNpgsql(connection, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(CommissionerMonitoringDbContext).Assembly.FullName);
                npgsql.MigrationsHistoryTable("__monitoring_ef_migrations_history", CommissionerMonitoringDbContext.Schema);
            });
        });
        return services;
    }
}
