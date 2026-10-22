using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRunAdjustmentBasicPayOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BasicPayOverride",
                table: "PayrollRunAdjustments",
                type: "decimal(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BasicPayOverride",
                table: "PayrollRunAdjustments");
        }
    }
}
