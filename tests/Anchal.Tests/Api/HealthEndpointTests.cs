using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Anchal.Tests.Api;

public sealed class HealthEndpointTests(HealthApplicationFactory factory) : IClassFixture<HealthApplicationFactory>
{
    // Break caught: process liveness becomes unavailable merely because the monitoring database is not configured.
    [Fact]
    public async Task ProcessHealthStaysHealthyWhenMonitoringDatabaseCannotBeResolved()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    // Break caught: the database-aware health endpoint reports success when its dedicated monitoring database cannot be resolved.
    [Fact]
    public async Task MonitoringHealthReportsUnhealthyWhenMonitoringDatabaseCannotBeResolved()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/commissioner-monitoring");

        Assert.True(response.StatusCode == HttpStatusCode.ServiceUnavailable, await response.Content.ReadAsStringAsync());
        var health = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(health);
        Assert.Equal("unhealthy", health.Status);
        Assert.Equal("anchal", health.Service);
    }

    [Fact]
    public void MissingRequiredDatabaseConfigurationFailsBeforeHttpStartup()
    {
        var error = Assert.Throws<InvalidOperationException>(() => AnchalApplication.Build([], builder =>
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CommissionerMonitoring"] = " ",
                ["Anchal:Jwt:Secret"] = "health-endpoint-test-secret-at-least-thirty-two-bytes",
                ["Anchal:Jwt:Issuer"] = "health-test",
                ["Anchal:Jwt:Audience"] = "health-test",
                ["Anchal:Jwt:ExpiryMinutes"] = "60"
            })));

        Assert.Contains("ConnectionStrings:CommissionerMonitoring", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidConfigurationFailsDuringApplicationConstructionBeforeHostedServerStartup()
    {
        var error = Assert.Throws<InvalidOperationException>(() => AnchalApplication.Build([], builder =>
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CommissionerMonitoring"] = " ",
                ["Anchal:Jwt:Secret"] = "health-endpoint-test-secret-at-least-thirty-two-bytes",
                ["Anchal:Jwt:Issuer"] = "health-test",
                ["Anchal:Jwt:Audience"] = "health-test",
                ["Anchal:Jwt:ExpiryMinutes"] = "60"
            })));

        Assert.Contains("ConnectionStrings:CommissionerMonitoring", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidRequiredJwtConfigurationFailsBeforeHttpStartup()
    {
        var error = Assert.Throws<InvalidOperationException>(() => AnchalApplication.Build([], builder =>
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CommissionerMonitoring"] = "Host=monitoring-host;Port=5432;Database=monitoring;Username=monitor;Password=test;SSL Mode=VerifyFull",
                ["CommissionerMonitoring:DatabaseEnvironment"] = "Development",
                ["CommissionerMonitoring:ExpectedHost"] = "monitoring-host",
                ["CommissionerMonitoring:ExpectedPort"] = "5432",
                ["CommissionerMonitoring:ExpectedDatabase"] = "monitoring",
                ["CommissionerMonitoring:ExpectedUsername"] = "monitor",
                ["Anchal:Jwt:Secret"] = "too-short",
                ["Anchal:Jwt:Issuer"] = "health-test",
                ["Anchal:Jwt:Audience"] = "health-test",
                ["Anchal:Jwt:ExpiryMinutes"] = "60"
            })));

        Assert.Contains("Anchal:Jwt:Secret", error.Message, StringComparison.Ordinal);
    }

    private sealed record HealthResponse(string Status, string Service);
}

public sealed class HealthApplicationFactory : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string?> _originalValues = new();

    public HealthApplicationFactory()
    {
        SetEnvironment("DOTNET_ENVIRONMENT", "Development");
        SetEnvironment("ASPNETCORE_ENVIRONMENT", "Development");
        SetEnvironment("Anchal__Jwt__Secret", "health-endpoint-test-secret-at-least-thirty-two-bytes");
        SetEnvironment("Anchal__Jwt__Issuer", "health-test");
        SetEnvironment("Anchal__Jwt__Audience", "health-test");
        SetEnvironment("Anchal__Jwt__ExpiryMinutes", "60");
        SetEnvironment("ConnectionStrings__CommissionerMonitoring", "Host=monitoring-host;Port=5432;Database=monitoring;Username=monitor;Password=test;SSL Mode=VerifyFull");
        SetEnvironment("CommissionerMonitoring__DatabaseEnvironment", "Development");
        SetEnvironment("CommissionerMonitoring__ExpectedHost", "monitoring-host");
        SetEnvironment("CommissionerMonitoring__ExpectedPort", "5432");
        SetEnvironment("CommissionerMonitoring__ExpectedDatabase", "monitoring");
        SetEnvironment("CommissionerMonitoring__ExpectedUsername", "monitor");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Anchal:Jwt:Secret"] = "health-endpoint-test-secret-at-least-thirty-two-bytes",
            ["Anchal:Jwt:Issuer"] = "health-test",
            ["Anchal:Jwt:Audience"] = "health-test",
            ["Anchal:Jwt:ExpiryMinutes"] = "60",
            ["ConnectionStrings:CommissionerMonitoring"] = "Host=monitoring-host;Port=5432;Database=monitoring;Username=monitor;Password=test;SSL Mode=VerifyFull",
            ["CommissionerMonitoring:DatabaseEnvironment"] = "Development",
            ["CommissionerMonitoring:ExpectedHost"] = "monitoring-host",
            ["CommissionerMonitoring:ExpectedPort"] = "5432",
            ["CommissionerMonitoring:ExpectedDatabase"] = "monitoring",
            ["CommissionerMonitoring:ExpectedUsername"] = "monitor"
        }));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            foreach (var (name, value) in _originalValues) Environment.SetEnvironmentVariable(name, value);
        base.Dispose(disposing);
    }

    private void SetEnvironment(string name, string value)
    {
        _originalValues[name] = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }
}
