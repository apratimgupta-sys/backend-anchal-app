using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anchal.Infrastructure.Persistence.Migrations.CommissionerMonitoring
{
    /// <inheritdoc />
    public partial class InitialCommissionerMonitoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "commissioner_monitoring");

            migrationBuilder.CreateTable(
                name: "monitoring_catalog_items",
                schema: "commissioner_monitoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_monitoring_catalog_items", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "monitoring_offices",
                schema: "commissioner_monitoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_monitoring_offices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "monitoring_users",
                schema: "commissioner_monitoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LoginName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    NormalizedLoginName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_monitoring_users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "monitoring_audit_events",
                schema: "commissioner_monitoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OfficeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AggregateType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    AggregateId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    BeforeJson = table.Column<string>(type: "jsonb", nullable: false),
                    AfterJson = table.Column<string>(type: "jsonb", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_monitoring_audit_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_monitoring_audit_events_monitoring_offices_OfficeId",
                        column: x => x.OfficeId,
                        principalSchema: "commissioner_monitoring",
                        principalTable: "monitoring_offices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_monitoring_audit_events_monitoring_users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalSchema: "commissioner_monitoring",
                        principalTable: "monitoring_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "monitoring_office_setups",
                schema: "commissioner_monitoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OfficeId = table.Column<Guid>(type: "uuid", nullable: false),
                    LockedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    LockedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_monitoring_office_setups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_monitoring_office_setups_monitoring_offices_OfficeId",
                        column: x => x.OfficeId,
                        principalSchema: "commissioner_monitoring",
                        principalTable: "monitoring_offices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_monitoring_office_setups_monitoring_users_LockedByUserId",
                        column: x => x.LockedByUserId,
                        principalSchema: "commissioner_monitoring",
                        principalTable: "monitoring_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "monitoring_reports",
                schema: "commissioner_monitoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OfficeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IsZeroActivity = table.Column<bool>(type: "boolean", nullable: false),
                    SubmittedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_monitoring_reports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_monitoring_reports_monitoring_offices_OfficeId",
                        column: x => x.OfficeId,
                        principalSchema: "commissioner_monitoring",
                        principalTable: "monitoring_offices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_monitoring_reports_monitoring_users_SubmittedByUserId",
                        column: x => x.SubmittedByUserId,
                        principalSchema: "commissioner_monitoring",
                        principalTable: "monitoring_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "monitoring_user_offices",
                schema: "commissioner_monitoring",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OfficeId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_monitoring_user_offices", x => new { x.UserId, x.OfficeId });
                    table.ForeignKey(
                        name: "FK_monitoring_user_offices_monitoring_offices_OfficeId",
                        column: x => x.OfficeId,
                        principalSchema: "commissioner_monitoring",
                        principalTable: "monitoring_offices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_monitoring_user_offices_monitoring_users_UserId",
                        column: x => x.UserId,
                        principalSchema: "commissioner_monitoring",
                        principalTable: "monitoring_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "monitoring_opening_balances",
                schema: "commissioner_monitoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OfficeSetupId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ItemKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_monitoring_opening_balances", x => x.Id);
                    table.CheckConstraint("ck_monitoring_opening_balance_nonnegative", "\"Count\" >= 0");
                    table.ForeignKey(
                        name: "FK_monitoring_opening_balances_monitoring_office_setups_Office~",
                        column: x => x.OfficeSetupId,
                        principalSchema: "commissioner_monitoring",
                        principalTable: "monitoring_office_setups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "monitoring_report_entries",
                schema: "commissioner_monitoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ItemKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ReceivedCount = table.Column<int>(type: "integer", nullable: false),
                    DisposedCount = table.Column<int>(type: "integer", nullable: false),
                    ClosingBalance = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_monitoring_report_entries", x => x.Id);
                    table.CheckConstraint("ck_monitoring_closing_nonnegative", "\"ClosingBalance\" >= 0");
                    table.CheckConstraint("ck_monitoring_disposed_nonnegative", "\"DisposedCount\" >= 0");
                    table.CheckConstraint("ck_monitoring_received_nonnegative", "\"ReceivedCount\" >= 0");
                    table.ForeignKey(
                        name: "FK_monitoring_report_entries_monitoring_reports_ReportId",
                        column: x => x.ReportId,
                        principalSchema: "commissioner_monitoring",
                        principalTable: "monitoring_reports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "monitoring_corrections",
                schema: "commissioner_monitoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    CorrectedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    BeforeJson = table.Column<string>(type: "jsonb", nullable: false),
                    AfterJson = table.Column<string>(type: "jsonb", nullable: false),
                    CorrectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_monitoring_corrections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_monitoring_corrections_monitoring_report_entries_EntryId",
                        column: x => x.EntryId,
                        principalSchema: "commissioner_monitoring",
                        principalTable: "monitoring_report_entries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_monitoring_corrections_monitoring_reports_ReportId",
                        column: x => x.ReportId,
                        principalSchema: "commissioner_monitoring",
                        principalTable: "monitoring_reports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_monitoring_corrections_monitoring_users_CorrectedByUserId",
                        column: x => x.CorrectedByUserId,
                        principalSchema: "commissioner_monitoring",
                        principalTable: "monitoring_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_monitoring_audit_events_ActorUserId",
                schema: "commissioner_monitoring",
                table: "monitoring_audit_events",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_monitoring_audit_events_AggregateType_AggregateId",
                schema: "commissioner_monitoring",
                table: "monitoring_audit_events",
                columns: new[] { "AggregateType", "AggregateId" });

            migrationBuilder.CreateIndex(
                name: "IX_monitoring_audit_events_OfficeId_OccurredAt",
                schema: "commissioner_monitoring",
                table: "monitoring_audit_events",
                columns: new[] { "OfficeId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_monitoring_catalog_items_Kind_Key",
                schema: "commissioner_monitoring",
                table: "monitoring_catalog_items",
                columns: new[] { "Kind", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_monitoring_corrections_CorrectedByUserId",
                schema: "commissioner_monitoring",
                table: "monitoring_corrections",
                column: "CorrectedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_monitoring_corrections_EntryId",
                schema: "commissioner_monitoring",
                table: "monitoring_corrections",
                column: "EntryId");

            migrationBuilder.CreateIndex(
                name: "IX_monitoring_corrections_ReportId_CorrectedAt",
                schema: "commissioner_monitoring",
                table: "monitoring_corrections",
                columns: new[] { "ReportId", "CorrectedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_monitoring_office_setups_LockedByUserId",
                schema: "commissioner_monitoring",
                table: "monitoring_office_setups",
                column: "LockedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_monitoring_office_setups_OfficeId",
                schema: "commissioner_monitoring",
                table: "monitoring_office_setups",
                column: "OfficeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_monitoring_offices_Code",
                schema: "commissioner_monitoring",
                table: "monitoring_offices",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_monitoring_opening_balances_OfficeSetupId_Kind_ItemKey",
                schema: "commissioner_monitoring",
                table: "monitoring_opening_balances",
                columns: new[] { "OfficeSetupId", "Kind", "ItemKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_monitoring_report_entries_ReportId_Kind_ItemKey",
                schema: "commissioner_monitoring",
                table: "monitoring_report_entries",
                columns: new[] { "ReportId", "Kind", "ItemKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_monitoring_reports_OfficeId_ReportingDate",
                schema: "commissioner_monitoring",
                table: "monitoring_reports",
                columns: new[] { "OfficeId", "ReportingDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_monitoring_reports_SubmittedByUserId",
                schema: "commissioner_monitoring",
                table: "monitoring_reports",
                column: "SubmittedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_monitoring_reports_UpdatedAt",
                schema: "commissioner_monitoring",
                table: "monitoring_reports",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_monitoring_user_offices_OfficeId",
                schema: "commissioner_monitoring",
                table: "monitoring_user_offices",
                column: "OfficeId");

            migrationBuilder.CreateIndex(
                name: "IX_monitoring_users_NormalizedLoginName",
                schema: "commissioner_monitoring",
                table: "monitoring_users",
                column: "NormalizedLoginName",
                unique: true);

            // Keep audit and locked setup records immutable even for writes outside EF.
            migrationBuilder.Sql("""
                CREATE FUNCTION commissioner_monitoring.reject_immutable_monitoring_change()
                RETURNS trigger LANGUAGE plpgsql AS $monitoring$
                BEGIN
                    RAISE EXCEPTION 'Monitoring audit, correction, and locked setup records are immutable';
                    RETURN OLD;
                END;
                $monitoring$;

                CREATE TRIGGER monitoring_audit_events_immutable
                BEFORE UPDATE OR DELETE ON commissioner_monitoring.monitoring_audit_events
                FOR EACH ROW EXECUTE FUNCTION commissioner_monitoring.reject_immutable_monitoring_change();

                CREATE TRIGGER monitoring_corrections_immutable
                BEFORE UPDATE OR DELETE ON commissioner_monitoring.monitoring_corrections
                FOR EACH ROW EXECUTE FUNCTION commissioner_monitoring.reject_immutable_monitoring_change();

                CREATE TRIGGER monitoring_office_setups_immutable
                BEFORE UPDATE OR DELETE ON commissioner_monitoring.monitoring_office_setups
                FOR EACH ROW EXECUTE FUNCTION commissioner_monitoring.reject_immutable_monitoring_change();

                CREATE TRIGGER monitoring_opening_balances_immutable
                BEFORE UPDATE OR DELETE ON commissioner_monitoring.monitoring_opening_balances
                FOR EACH ROW EXECUTE FUNCTION commissioner_monitoring.reject_immutable_monitoring_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "monitoring_audit_events",
                schema: "commissioner_monitoring");

            migrationBuilder.DropTable(
                name: "monitoring_catalog_items",
                schema: "commissioner_monitoring");

            migrationBuilder.DropTable(
                name: "monitoring_corrections",
                schema: "commissioner_monitoring");

            migrationBuilder.DropTable(
                name: "monitoring_opening_balances",
                schema: "commissioner_monitoring");

            migrationBuilder.DropTable(
                name: "monitoring_user_offices",
                schema: "commissioner_monitoring");

            migrationBuilder.DropTable(
                name: "monitoring_report_entries",
                schema: "commissioner_monitoring");

            migrationBuilder.DropTable(
                name: "monitoring_office_setups",
                schema: "commissioner_monitoring");

            migrationBuilder.DropTable(
                name: "monitoring_reports",
                schema: "commissioner_monitoring");

            migrationBuilder.DropTable(
                name: "monitoring_offices",
                schema: "commissioner_monitoring");

            migrationBuilder.DropTable(
                name: "monitoring_users",
                schema: "commissioner_monitoring");

            migrationBuilder.Sql(
                "DROP FUNCTION commissioner_monitoring.reject_immutable_monitoring_change();");
        }
    }
}
