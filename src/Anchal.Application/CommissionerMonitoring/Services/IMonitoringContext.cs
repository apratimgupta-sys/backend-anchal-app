using Microsoft.EntityFrameworkCore;
using Anchal.Domain.CommissionerMonitoring;

namespace Anchal.Application.CommissionerMonitoring.Services;

/// <summary>Only the dedicated monitoring database may implement this application boundary.</summary>
public interface IMonitoringContext
{
    DbSet<MonitoringUser> Users { get; }
    DbSet<MonitoringOffice> Offices { get; }
    DbSet<MonitoringCatalogItem> CatalogItems { get; }
    DbSet<MonitoringOfficeSetup> OfficeSetups { get; }
    DbSet<MonitoringReport> Reports { get; }
    DbSet<MonitoringReportEntry> ReportEntries { get; }
    DbSet<MonitoringCorrection> Corrections { get; }
    DbSet<MonitoringAuditEvent> AuditEvents { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    // Implementations serialize same-office mutations across processes, commit atomically,
    // and discard tracked changes on failure. Validation runs inside the transaction.
    Task<T> MutateOfficeAsync<T>(Guid officeId, Func<Task<T>> operation, CancellationToken cancellationToken);
}
