using Anchal.Domain.CommissionerMonitoring;
using Anchal.Infrastructure.CommissionerMonitoring;
using Anchal.Infrastructure.CommissionerMonitoring.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Anchal.Tests.CommissionerMonitoring.Persistence;

public sealed class CommissionerMonitoringPersistenceTests
{
    [Fact]
    public void DedicatedConnectionIsRequiredEvenWhenDefaultConnectionExists()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=legacy;Database=legacy;Username=legacy;Password=test"
        }).Build();
        using var provider = new ServiceCollection()
            .AddCommissionerMonitoringPersistence(configuration, new TestHostEnvironment())
            .BuildServiceProvider();

        var error = Assert.Throws<InvalidOperationException>(() =>
            provider.GetRequiredService<CommissionerMonitoringDbContext>());

        Assert.Contains("ConnectionStrings:CommissionerMonitoring", error.Message);
    }

    [Fact]
    public void MonitoringContextUsesDedicatedConnectionAndMapsOnlyMonitoringTables()
    {
        using var provider = Services().BuildServiceProvider();
        using var context = provider.GetRequiredService<CommissionerMonitoringDbContext>();

        Assert.Contains("monitoring-host", context.Database.GetDbConnection().DataSource);
        Assert.NotNull(context.Model.FindEntityType(typeof(MonitoringUser)));
        Assert.All(context.Model.GetEntityTypes(), entity =>
            Assert.StartsWith("monitoring_", entity.GetTableName()));
    }

    [Fact]
    public void ReportsHaveOfficeScopedDateKeyAndUseDedicatedSchema()
    {
        using var context = NewRelationalContext();
        var report = context.Model.FindEntityType(typeof(MonitoringReport))!;

        Assert.Equal("date", report.FindProperty(nameof(MonitoringReport.ReportingDate))!.GetColumnType());
        Assert.Equal("commissioner_monitoring", report.GetSchema());
        Assert.Contains(report.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(
                [nameof(MonitoringReport.OfficeId), nameof(MonitoringReport.ReportingDate)]));
    }

    [Fact]
    public async Task AuditEventsCannotBeChangedAfterInsertion()
    {
        await using var context = NewInMemoryContext();
        var audit = new MonitoringAuditEvent { Id = Guid.NewGuid(), OfficeId = Guid.NewGuid(), Action = "report.submitted",
            AggregateType = "report", AggregateId = Guid.NewGuid(), OccurredAt = DateTimeOffset.UtcNow };
        context.AuditEvents.Add(audit);
        await context.SaveChangesAsync();

        audit.Action = "report.deleted";

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public void MigrationScriptContainsOnlyMonitoringSchemaAndHistory()
    {
        using var provider = Services().BuildServiceProvider();
        using var context = provider.GetRequiredService<CommissionerMonitoringDbContext>();
        var sql = context.GetService<IMigrator>().GenerateScript();

        Assert.Contains("CREATE TABLE commissioner_monitoring.monitoring_reports", sql);
        Assert.Contains("commissioner_monitoring.__monitoring_ef_migrations_history", sql);
        Assert.DoesNotContain("AspNetUsers", sql);
    }

    private static IServiceCollection Services() => new ServiceCollection()
        .AddCommissionerMonitoringPersistence(Configuration(), new TestHostEnvironment());

    private static IConfiguration Configuration() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["CommissionerMonitoring:DatabaseEnvironment"] = "Staging",
        ["CommissionerMonitoring:ExpectedHost"] = "monitoring-host",
        ["CommissionerMonitoring:ExpectedPort"] = "5432",
        ["CommissionerMonitoring:ExpectedDatabase"] = "monitoring",
        ["CommissionerMonitoring:ExpectedUsername"] = "monitor",
        ["ConnectionStrings:CommissionerMonitoring"] = "Host=monitoring-host;Database=monitoring;Username=monitor;Password=test;SSL Mode=VerifyFull"
    }).Build();

    private static CommissionerMonitoringDbContext NewInMemoryContext() => new(new DbContextOptionsBuilder<CommissionerMonitoringDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static CommissionerMonitoringDbContext NewRelationalContext() => new(new DbContextOptionsBuilder<CommissionerMonitoringDbContext>()
        .UseNpgsql("Host=localhost;Database=monitoring;Username=test;Password=test").Options);

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Staging;
        public string ApplicationName { get; set; } = "Anchal.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
