using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anchal.Infrastructure.Persistence.Migrations.CommissionerMonitoring
{
    /// <inheritdoc />
    public partial class AddFieldVisitDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DetailsJson",
                schema: "commissioner_monitoring",
                table: "monitoring_field_visits",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DetailsJson",
                schema: "commissioner_monitoring",
                table: "monitoring_field_visits");
        }
    }
}
