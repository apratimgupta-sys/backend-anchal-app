using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Anchal.Infrastructure.CommissionerMonitoring.Persistence;

public static class CommissionerMonitoringConnectionValidator
{
    public static string Validate(IConfiguration configuration) => Validate(configuration,
        configuration["DOTNET_ENVIRONMENT"] ?? configuration["ASPNETCORE_ENVIRONMENT"] ?? "Production");

    public static string Validate(IConfiguration configuration, string environment)
    {
        var connection = configuration.GetConnectionString("CommissionerMonitoring");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("ConnectionStrings:CommissionerMonitoring is required for Commissioner Monitoring persistence.");

        var target = Parse(connection);
        var host = target.Host ?? string.Empty;
        if (environment is not ("Development" or "Staging" or "Production") ||
            !string.Equals(configuration["CommissionerMonitoring:DatabaseEnvironment"], environment, StringComparison.Ordinal))
            throw new InvalidOperationException("CommissionerMonitoring:DatabaseEnvironment must match Development, Staging or Production on the host.");

        if (host.Contains(',') || host.Contains('/') ||
            !string.Equals(host, configuration["CommissionerMonitoring:ExpectedHost"], StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(target.Database, configuration["CommissionerMonitoring:ExpectedDatabase"], StringComparison.Ordinal) ||
            !string.Equals(target.Username, configuration["CommissionerMonitoring:ExpectedUsername"], StringComparison.Ordinal) ||
            !int.TryParse(configuration["CommissionerMonitoring:ExpectedPort"], out var port) || target.Port != port)
            throw new InvalidOperationException("Commissioner Monitoring database target must match the environment's explicit host, port, database and username pin; multiple hosts and socket paths are not supported.");

        if (environment != "Production" && new[] { host, target.Database ?? string.Empty, target.Username ?? string.Empty }
            .Any(value => value.Contains("prod", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Non-production Commissioner Monitoring cannot use a production-like target.");

        var loopback = host is "localhost" or "127.0.0.1" or "::1";
        if ((!loopback || environment == "Production") && target.SslMode != SslMode.VerifyFull)
            throw new InvalidOperationException("Remote and production Commissioner Monitoring connections require SSL Mode=VerifyFull.");

        return connection;
    }

    private static NpgsqlConnectionStringBuilder Parse(string connection)
    {
        try
        {
            var target = new NpgsqlConnectionStringBuilder(connection);
            if (string.IsNullOrWhiteSpace(target.Host) || string.IsNullOrWhiteSpace(target.Database) || string.IsNullOrWhiteSpace(target.Username))
                throw new InvalidOperationException("Explicit database host, database and username are required for Commissioner Monitoring isolation.");
            return target;
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("Invalid database connection configuration for Commissioner Monitoring isolation.");
        }
    }
}
