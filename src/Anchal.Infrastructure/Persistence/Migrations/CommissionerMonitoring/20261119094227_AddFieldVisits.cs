using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anchal.Infrastructure.Persistence.Migrations.CommissionerMonitoring
{
    /// <inheritdoc />
    public partial class AddFieldVisits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Designation",
                schema: "commissioner_monitoring",
                table: "monitoring_users",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                schema: "commissioner_monitoring",
                table: "monitoring_users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "monitoring_field_visits",
                schema: "commissioner_monitoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OfficeId = table.Column<Guid>(type: "uuid", nullable: false),
                    OfficerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ward = table.Column<int>(type: "integer", nullable: false),
                    ServiceId = table.Column<int>(type: "integer", nullable: false),
                    VisitDate = table.Column<DateOnly>(type: "date", nullable: false),
                    VisitTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    AccuracyMetres = table.Column<double>(type: "double precision", nullable: false),
                    GpsCapturedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VerifiedOnSite = table.Column<bool>(type: "boolean", nullable: false),
                    IssueAction = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    PhotoOneKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    PhotoTwoKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    VoidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VoidReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    VoidedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_monitoring_field_visits", x => x.Id);
                    table.CheckConstraint("ck_monitoring_field_visit_accuracy", "\"AccuracyMetres\" >= 0");
                    table.CheckConstraint("ck_monitoring_field_visit_service", "\"ServiceId\" BETWEEN 1 AND 25");
                    table.CheckConstraint("ck_monitoring_field_visit_ward", "\"Ward\" BETWEEN 1 AND 53");
                    table.ForeignKey(
                        name: "FK_monitoring_field_visits_monitoring_offices_OfficeId",
                        column: x => x.OfficeId,
                        principalSchema: "commissioner_monitoring",
                        principalTable: "monitoring_offices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_monitoring_field_visits_monitoring_users_OfficerUserId",
                        column: x => x.OfficerUserId,
                        principalSchema: "commissioner_monitoring",
                        principalTable: "monitoring_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_monitoring_field_visits_monitoring_users_VoidedByUserId",
                        column: x => x.VoidedByUserId,
                        principalSchema: "commissioner_monitoring",
                        principalTable: "monitoring_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_monitoring_field_visits_OfficeId_VisitDate",
                schema: "commissioner_monitoring",
                table: "monitoring_field_visits",
                columns: new[] { "OfficeId", "VisitDate" });

            migrationBuilder.CreateIndex(
                name: "IX_monitoring_field_visits_OfficerUserId_VisitDate",
                schema: "commissioner_monitoring",
                table: "monitoring_field_visits",
                columns: new[] { "OfficerUserId", "VisitDate" });

            migrationBuilder.CreateIndex(
                name: "IX_monitoring_field_visits_VoidedByUserId",
                schema: "commissioner_monitoring",
                table: "monitoring_field_visits",
                column: "VoidedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "monitoring_field_visits",
                schema: "commissioner_monitoring");

            migrationBuilder.DropColumn(
                name: "Designation",
                schema: "commissioner_monitoring",
                table: "monitoring_users");

            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                schema: "commissioner_monitoring",
                table: "monitoring_users");
        }
    }
}
