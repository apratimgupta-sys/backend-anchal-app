using Anchal.Application.CommissionerMonitoring.Auth;
using Anchal.Domain.CommissionerMonitoring;
using Anchal.Infrastructure.CommissionerMonitoring;
using Anchal.Infrastructure.CommissionerMonitoring.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Anchal.Tests.CommissionerMonitoring.FieldVisits;

public sealed class FieldVisitModelTests
{
    [Fact]
    public void FieldVisitsAreMappedInTheMonitoringSchema()
    {
        using var context = Context();
        var entity = context.Model.FindEntityType(typeof(MonitoringFieldVisit));

        Assert.NotNull(entity);
        Assert.Equal("monitoring_field_visits", entity!.GetTableName());
        Assert.Equal(CommissionerMonitoringDbContext.Schema, entity.GetSchema());
    }

    [Fact]
    public void AVisitRecordsWhoFiledItWhereAndWhen()
    {
        using var context = Context();
        var entity = context.Model.FindEntityType(typeof(MonitoringFieldVisit))!;

        foreach (var property in new[]
                 {
                     nameof(MonitoringFieldVisit.OfficeId), nameof(MonitoringFieldVisit.OfficerUserId),
                     nameof(MonitoringFieldVisit.Ward), nameof(MonitoringFieldVisit.ServiceId),
                     nameof(MonitoringFieldVisit.VisitDate), nameof(MonitoringFieldVisit.VisitTime),
                     nameof(MonitoringFieldVisit.Latitude), nameof(MonitoringFieldVisit.Longitude),
                     nameof(MonitoringFieldVisit.AccuracyMetres), nameof(MonitoringFieldVisit.GpsCapturedAt),
                     nameof(MonitoringFieldVisit.ReceivedAt), nameof(MonitoringFieldVisit.VerifiedOnSite),
                     nameof(MonitoringFieldVisit.IssueAction)
                 })
            Assert.NotNull(entity.FindProperty(property));
    }

    [Fact]
    public void AVisitCanBeVoidedWithoutBeingDeleted()
    {
        using var context = Context();
        var entity = context.Model.FindEntityType(typeof(MonitoringFieldVisit))!;

        Assert.True(entity.FindProperty(nameof(MonitoringFieldVisit.VoidedAt))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(MonitoringFieldVisit.VoidReason))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(MonitoringFieldVisit.VoidedByUserId))!.IsNullable);
    }

    [Fact]
    public void PhotographColumnsExistAndStayEmptyInThisIteration()
    {
        using var context = Context();
        var entity = context.Model.FindEntityType(typeof(MonitoringFieldVisit))!;

        Assert.True(entity.FindProperty(nameof(MonitoringFieldVisit.PhotoOneKey))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(MonitoringFieldVisit.PhotoTwoKey))!.IsNullable);
    }

    [Fact]
    public void WardServiceAndAccuracyAreBoundedInTheDatabase()
    {
        // Asserted against the migration, which is what actually reaches the database.
        var migration = Directory
            .EnumerateFiles(RepositoryRoot(), "*_AddFieldVisits.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText).Single()
            .Replace("\\\"", "\""); // the migration is C# source, so its SQL quotes are escaped

        Assert.Contains("ck_monitoring_field_visit_ward", migration);
        Assert.Contains("\"Ward\" BETWEEN 1 AND 53", migration);
        Assert.Contains("ck_monitoring_field_visit_service", migration);
        Assert.Contains("\"ServiceId\" BETWEEN 1 AND 25", migration);
        Assert.Contains("ck_monitoring_field_visit_accuracy", migration);
    }

    [Fact]
    public void AnOfficersVisitsAreIndexedForTheirOwnList()
    {
        using var context = Context();
        var indexes = context.Model.FindEntityType(typeof(MonitoringFieldVisit))!.GetIndexes()
            .Select(x => string.Join(",", x.Properties.Select(p => p.Name))).ToArray();

        Assert.Contains(indexes, x => x.Contains(nameof(MonitoringFieldVisit.OfficerUserId)));
        Assert.Contains(indexes, x => x.Contains(nameof(MonitoringFieldVisit.VisitDate)));
    }

    [Fact]
    public void AnOfficerCarriesADesignationAndCanBeForcedToChangeTheirPassword()
    {
        using var context = Context();
        var user = context.Model.FindEntityType(typeof(MonitoringUser))!;

        Assert.NotNull(user.FindProperty(nameof(MonitoringUser.Designation)));
        Assert.NotNull(user.FindProperty(nameof(MonitoringUser.MustChangePassword)));
    }

    [Fact]
    public void TheFieldOfficerRoleHasItsOwnPolicy()
    {
        var options = new AuthorizationOptions();
        MonitoringPolicies.AddTo(options);

        Assert.Equal("FieldOfficer", MonitoringPolicies.FieldOfficerRole);
        Assert.NotNull(options.GetPolicy(MonitoringPolicies.FieldOfficer));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) ||
                File.Exists(Path.Combine(directory.FullName, "Anchal.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private static CommissionerMonitoringDbContext Context() =>
        new ServiceCollection()
            .AddCommissionerMonitoringPersistence(Configuration(), new TestHostEnvironment())
            .BuildServiceProvider()
            .GetRequiredService<CommissionerMonitoringDbContext>();

    private static IConfiguration Configuration() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
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
