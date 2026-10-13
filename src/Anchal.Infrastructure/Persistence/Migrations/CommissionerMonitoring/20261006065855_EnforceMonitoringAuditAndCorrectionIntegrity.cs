using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anchal.Infrastructure.Persistence.Migrations.CommissionerMonitoring
{
    /// <inheritdoc />
    public partial class EnforceMonitoringAuditAndCorrectionIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_monitoring_corrections_monitoring_report_entries_EntryId",
                schema: "commissioner_monitoring",
                table: "monitoring_corrections");

            migrationBuilder.DropIndex(
                name: "IX_monitoring_corrections_EntryId",
                schema: "commissioner_monitoring",
                table: "monitoring_corrections");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_monitoring_report_entries_ReportId_Id",
                schema: "commissioner_monitoring",
                table: "monitoring_report_entries",
                columns: new[] { "ReportId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_monitoring_corrections_ReportId_EntryId",
                schema: "commissioner_monitoring",
                table: "monitoring_corrections",
                columns: new[] { "ReportId", "EntryId" });

            migrationBuilder.AddForeignKey(
                name: "FK_monitoring_corrections_monitoring_report_entries_ReportId_E~",
                schema: "commissioner_monitoring",
                table: "monitoring_corrections",
                columns: new[] { "ReportId", "EntryId" },
                principalSchema: "commissioner_monitoring",
                principalTable: "monitoring_report_entries",
                principalColumns: new[] { "ReportId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                CREATE FUNCTION commissioner_monitoring.reject_report_submission_change()
                RETURNS trigger LANGUAGE plpgsql AS $monitoring$
                BEGIN
                    RAISE EXCEPTION 'Monitoring report submission audit fields are immutable';
                    RETURN OLD;
                END;
                $monitoring$;

                CREATE TRIGGER monitoring_reports_submission_immutable
                BEFORE UPDATE ON commissioner_monitoring.monitoring_reports
                FOR EACH ROW
                WHEN (OLD."SubmittedByUserId" IS DISTINCT FROM NEW."SubmittedByUserId"
                   OR OLD."SubmittedAt" IS DISTINCT FROM NEW."SubmittedAt")
                EXECUTE FUNCTION commissioner_monitoring.reject_report_submission_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER monitoring_reports_submission_immutable
                    ON commissioner_monitoring.monitoring_reports;
                DROP FUNCTION commissioner_monitoring.reject_report_submission_change();
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_monitoring_corrections_monitoring_report_entries_ReportId_E~",
                schema: "commissioner_monitoring",
                table: "monitoring_corrections");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_monitoring_report_entries_ReportId_Id",
                schema: "commissioner_monitoring",
                table: "monitoring_report_entries");

            migrationBuilder.DropIndex(
                name: "IX_monitoring_corrections_ReportId_EntryId",
                schema: "commissioner_monitoring",
                table: "monitoring_corrections");

            migrationBuilder.CreateIndex(
                name: "IX_monitoring_corrections_EntryId",
                schema: "commissioner_monitoring",
                table: "monitoring_corrections",
                column: "EntryId");

            migrationBuilder.AddForeignKey(
                name: "FK_monitoring_corrections_monitoring_report_entries_EntryId",
                schema: "commissioner_monitoring",
                table: "monitoring_corrections",
                column: "EntryId",
                principalSchema: "commissioner_monitoring",
                principalTable: "monitoring_report_entries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
