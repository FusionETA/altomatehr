using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSupervisorSlaMinutes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SupervisorSlaMinutes",
                table: "Organizations",
                type: "int",
                nullable: false,
                // 60, not the scaffolded 0: this backfills every existing org
                // and a zero-minute SLA would mark every decision ever made as
                // slow. Matches the entity default so the report and the
                // settings screen cannot disagree.
                defaultValue: 60);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SupervisorSlaMinutes",
                table: "Organizations");
        }
    }
}
