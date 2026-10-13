using Microsoft.EntityFrameworkCore;
using Anchal.Application.CommissionerMonitoring.Services;
using Anchal.Domain.CommissionerMonitoring;

namespace Anchal.Infrastructure.CommissionerMonitoring.Persistence;

public sealed partial class CommissionerMonitoringDbContext(DbContextOptions<CommissionerMonitoringDbContext> options)
    : DbContext(options), IMonitoringContext
{
    public const string Schema = "commissioner_monitoring";

    public DbSet<MonitoringUser> Users => Set<MonitoringUser>();
    public DbSet<MonitoringOffice> Offices => Set<MonitoringOffice>();
    public DbSet<MonitoringUserOffice> UserOffices => Set<MonitoringUserOffice>();
    public DbSet<MonitoringCatalogItem> CatalogItems => Set<MonitoringCatalogItem>();
    public DbSet<MonitoringOfficeSetup> OfficeSetups => Set<MonitoringOfficeSetup>();
    public DbSet<MonitoringOpeningBalance> OpeningBalances => Set<MonitoringOpeningBalance>();
    public DbSet<MonitoringReport> Reports => Set<MonitoringReport>();
    public DbSet<MonitoringReportEntry> ReportEntries => Set<MonitoringReportEntry>();
    public DbSet<MonitoringCorrection> Corrections => Set<MonitoringCorrection>();
    public DbSet<MonitoringAuditEvent> AuditEvents => Set<MonitoringAuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<MonitoringUser>(entity =>
        {
            entity.ToTable("monitoring_users");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.LoginName).HasMaxLength(120).IsRequired();
            entity.Property(x => x.NormalizedLoginName).HasMaxLength(120).IsRequired();
            entity.Property(x => x.PasswordHash).HasMaxLength(512).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Role).HasMaxLength(40).IsRequired();
            entity.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
            entity.HasIndex(x => x.NormalizedLoginName).IsUnique();
        });

        modelBuilder.Entity<MonitoringOffice>(entity =>
        {
            entity.ToTable("monitoring_offices");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Code).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(160).IsRequired();
            entity.HasIndex(x => x.Code).IsUnique();
        });

        modelBuilder.Entity<MonitoringUserOffice>(entity =>
        {
            entity.ToTable("monitoring_user_offices");
            entity.HasKey(x => new { x.UserId, x.OfficeId });
            entity.HasOne(x => x.User).WithMany(x => x.OfficeAssignments)
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Office).WithMany(x => x.UserAssignments)
                .HasForeignKey(x => x.OfficeId).OnDelete(DeleteBehavior.Restrict);
            entity.Property(x => x.AssignedAt).HasColumnType("timestamp with time zone");
            entity.HasIndex(x => x.OfficeId);
        });

        modelBuilder.Entity<MonitoringCatalogItem>(entity =>
        {
            entity.ToTable("monitoring_catalog_items");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.Key).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(160).IsRequired();
            entity.HasIndex(x => new { x.Kind, x.Key }).IsUnique();
        });

        modelBuilder.Entity<MonitoringOfficeSetup>(entity =>
        {
            entity.ToTable("monitoring_office_setups");
            entity.HasKey(x => x.Id);
            entity.HasOne(x => x.Office).WithMany().HasForeignKey(x => x.OfficeId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.LockedByUser).WithMany().HasForeignKey(x => x.LockedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.Property(x => x.EffectiveDate).HasColumnType("date");
            entity.Property(x => x.LockedAt).HasColumnType("timestamp with time zone");
            entity.HasIndex(x => x.OfficeId).IsUnique();
        });

        modelBuilder.Entity<MonitoringOpeningBalance>(entity =>
        {
            entity.ToTable("monitoring_opening_balances", table =>
                table.HasCheckConstraint("ck_monitoring_opening_balance_nonnegative", "\"Count\" >= 0"));
            entity.HasKey(x => x.Id);
            entity.HasOne(x => x.OfficeSetup).WithMany(x => x.OpeningBalances)
                .HasForeignKey(x => x.OfficeSetupId).OnDelete(DeleteBehavior.Restrict);
            entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.ItemKey).HasMaxLength(80).IsRequired();
            entity.HasIndex(x => new { x.OfficeSetupId, x.Kind, x.ItemKey }).IsUnique();
        });

        modelBuilder.Entity<MonitoringReport>(entity =>
        {
            entity.ToTable("monitoring_reports");
            entity.HasKey(x => x.Id);
            entity.HasOne(x => x.Office).WithMany().HasForeignKey(x => x.OfficeId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.SubmittedByUser).WithMany().HasForeignKey(x => x.SubmittedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.Property(x => x.ReportingDate).HasColumnType("date");
            entity.Property(x => x.SubmittedAt).HasColumnType("timestamp with time zone");
            entity.Property(x => x.UpdatedAt).HasColumnType("timestamp with time zone");
            entity.HasIndex(x => new { x.OfficeId, x.ReportingDate }).IsUnique();
            entity.HasIndex(x => x.UpdatedAt);
        });

        modelBuilder.Entity<MonitoringReportEntry>(entity =>
        {
            entity.ToTable("monitoring_report_entries", table =>
            {
                table.HasCheckConstraint("ck_monitoring_received_nonnegative", "\"ReceivedCount\" >= 0");
                table.HasCheckConstraint("ck_monitoring_disposed_nonnegative", "\"DisposedCount\" >= 0");
                table.HasCheckConstraint("ck_monitoring_closing_nonnegative", "\"ClosingBalance\" >= 0");
            });
            entity.HasKey(x => x.Id);
            entity.HasOne(x => x.Report).WithMany(x => x.Entries).HasForeignKey(x => x.ReportId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasAlternateKey(x => new { x.ReportId, x.Id });
            entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.ItemKey).HasMaxLength(80).IsRequired();
            entity.HasIndex(x => new { x.ReportId, x.Kind, x.ItemKey }).IsUnique();
        });

        modelBuilder.Entity<MonitoringCorrection>(entity =>
        {
            entity.ToTable("monitoring_corrections");
            entity.HasKey(x => x.Id);
            entity.HasOne(x => x.Report).WithMany().HasForeignKey(x => x.ReportId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Entry).WithMany()
                .HasForeignKey(x => new { x.ReportId, x.EntryId })
                .HasPrincipalKey(x => new { x.ReportId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.CorrectedByUser).WithMany().HasForeignKey(x => x.CorrectedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.BeforeJson).HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.AfterJson).HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.CorrectedAt).HasColumnType("timestamp with time zone");
            entity.HasIndex(x => new { x.ReportId, x.CorrectedAt });
        });

        modelBuilder.Entity<MonitoringAuditEvent>(entity =>
        {
            entity.ToTable("monitoring_audit_events");
            entity.HasKey(x => x.Id);
            entity.HasOne(x => x.Office).WithMany().HasForeignKey(x => x.OfficeId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ActorUser).WithMany().HasForeignKey(x => x.ActorUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.Property(x => x.AggregateType).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Action).HasMaxLength(80).IsRequired();
            entity.Property(x => x.BeforeJson).HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.AfterJson).HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.OccurredAt).HasColumnType("timestamp with time zone");
            entity.HasIndex(x => new { x.OfficeId, x.OccurredAt });
            entity.HasIndex(x => new { x.AggregateType, x.AggregateId });
        });
    }

    public override int SaveChanges()
    {
        GuardAppendOnlyRecords();
        return base.SaveChanges();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardAppendOnlyRecords();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        GuardAppendOnlyRecords();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardAppendOnlyRecords();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void GuardAppendOnlyRecords()
    {
        ChangeTracker.DetectChanges();
        if (ChangeTracker.Entries<MonitoringReport>().Any(entry =>
            entry.State == EntityState.Modified &&
            (entry.Property(report => report.SubmittedByUserId).OriginalValue != entry.Entity.SubmittedByUserId ||
             entry.Property(report => report.SubmittedAt).OriginalValue != entry.Entity.SubmittedAt)))
        {
            throw new InvalidOperationException("Monitoring report submission audit fields are immutable after insertion.");
        }

        if (ChangeTracker.Entries().Any(entry =>
            (entry.Entity is MonitoringAuditEvent or MonitoringCorrection or MonitoringOfficeSetup or MonitoringOpeningBalance) &&
            entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Monitoring audit, correction, and locked office setup records are append-only.");
        }
    }
}
