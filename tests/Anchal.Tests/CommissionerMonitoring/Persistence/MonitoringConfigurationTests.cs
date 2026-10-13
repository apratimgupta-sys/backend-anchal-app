using Anchal.Infrastructure.CommissionerMonitoring;
using Anchal.Infrastructure.CommissionerMonitoring.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Anchal.Tests.CommissionerMonitoring.Persistence;

public sealed class MonitoringConfigurationTests
{
    [Theory]
    [InlineData("db.other.supabase.co")]
    [InlineData("db.production.supabase.co")]
    public void UnsafeOrUnpinnedTargetsAreRejectedWithoutLeakingPasswords(string host)
    {
        var configuration = Config();
        configuration["ConnectionStrings:CommissionerMonitoring"] =
            $"Host={host};Database=monitoring;Username=monitor;Password=private-test-password;SSL Mode=VerifyFull";
        using var provider = new ServiceCollection()
            .AddCommissionerMonitoringPersistence(configuration, new TestHostEnvironment())
            .BuildServiceProvider();

        var error = Assert.Throws<InvalidOperationException>(() =>
            provider.GetRequiredService<CommissionerMonitoringDbContext>());

        Assert.DoesNotContain("private-test-password", error.ToString());
    }

    [Theory]
    [InlineData("Disable")]
    [InlineData("Require")]
    public void RemoteTargetsRequireVerifyFull(string sslMode)
    {
        var configuration = Config();
        configuration["ConnectionStrings:CommissionerMonitoring"] =
            $"Host=monitoring-host;Database=monitoring;Username=monitor;Password=test;SSL Mode={sslMode}";
        using var provider = new ServiceCollection()
            .AddCommissionerMonitoringPersistence(configuration, new TestHostEnvironment())
            .BuildServiceProvider();

        var error = Assert.Throws<InvalidOperationException>(() =>
            provider.GetRequiredService<CommissionerMonitoringDbContext>());

        Assert.Equal("Remote and production Commissioner Monitoring connections require SSL Mode=VerifyFull.", error.Message);
    }

    [Fact]
    public void DirectValidatorRejectsMissingDedicatedConnection()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            CommissionerMonitoringConnectionValidator.Validate(new ConfigurationBuilder().Build()));

        Assert.Contains("ConnectionStrings:CommissionerMonitoring", error.Message);
    }

    private static IConfigurationRoot Config() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["CommissionerMonitoring:DatabaseEnvironment"] = "Staging",
        ["CommissionerMonitoring:ExpectedHost"] = "monitoring-host",
        ["CommissionerMonitoring:ExpectedPort"] = "5432",
        ["CommissionerMonitoring:ExpectedDatabase"] = "monitoring",
        ["CommissionerMonitoring:ExpectedUsername"] = "monitor",
        ["ConnectionStrings:CommissionerMonitoring"] = "Host=monitoring-host;Database=monitoring;Username=monitor;Password=test;SSL Mode=VerifyFull"
    }).Build();

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Staging;
        public string ApplicationName { get; set; } = "Anchal.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
