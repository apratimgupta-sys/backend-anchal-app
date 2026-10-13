using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Anchal.Infrastructure.CommissionerMonitoring.Persistence;

public sealed class CommissionerMonitoringDbContextFactory : IDesignTimeDbContextFactory<CommissionerMonitoringDbContext>
{
    public CommissionerMonitoringDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__CommissionerMonitoring");
        if (string.IsNullOrWhiteSpace(connection))
        {
            throw new InvalidOperationException(
                "Set ConnectionStrings__CommissionerMonitoring to generate Commissioner Monitoring migrations.");
        }

        // Deliberately environment-only: never load the API's settings or execute its startup/seeder.
        string[] keys = ["ConnectionStrings:CommissionerMonitoring",
            "CommissionerMonitoring:DatabaseEnvironment", "CommissionerMonitoring:ExpectedHost",
            "CommissionerMonitoring:ExpectedPort", "CommissionerMonitoring:ExpectedDatabase",
            "CommissionerMonitoring:ExpectedUsername"];
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(keys.ToDictionary(
            key => key, key => Environment.GetEnvironmentVariable(key.Replace(":", "__")))).Build();
        var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
        var validatedConnection = CommissionerMonitoringConnectionValidator.Validate(configuration, environment);

        var options = new DbContextOptionsBuilder<CommissionerMonitoringDbContext>()
            .UseNpgsql(validatedConnection, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(CommissionerMonitoringDbContext).Assembly.FullName);
                npgsql.MigrationsHistoryTable(
                    "__monitoring_ef_migrations_history", CommissionerMonitoringDbContext.Schema);
            })
            .Options;

        return new CommissionerMonitoringDbContext(options);
    }
}
