using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anchal.Infrastructure.Persistence.Migrations.CommissionerMonitoring
{
    /// <inheritdoc />
    public partial class AddFieldVisitRequestId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Fingerprint",
                schema: "commissioner_monitoring",
                table: "monitoring_field_visits",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RequestId",
                schema: "commissioner_monitoring",
                table: "monitoring_field_visits",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_monitoring_field_visits_OfficerUserId_RequestId",
                schema: "commissioner_monitoring",
                table: "monitoring_field_visits",
                columns: new[] { "OfficerUserId", "RequestId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_monitoring_field_visits_OfficerUserId_RequestId",
                schema: "commissioner_monitoring",
                table: "monitoring_field_visits");

            migrationBuilder.DropColumn(
                name: "Fingerprint",
                schema: "commissioner_monitoring",
                table: "monitoring_field_visits");

            migrationBuilder.DropColumn(
                name: "RequestId",
                schema: "commissioner_monitoring",
                table: "monitoring_field_visits");
        }
    }
}
