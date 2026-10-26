using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class UnpaidLeaveCarryForward : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CarriedDays",
                table: "PayslipLineItems",
                type: "decimal(6,2)",
                precision: 6,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CarriedFromMonth",
                table: "PayslipLineItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CarriedFromYear",
                table: "PayslipLineItems",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CarriedDays",
                table: "PayslipLineItems");

            migrationBuilder.DropColumn(
                name: "CarriedFromMonth",
                table: "PayslipLineItems");

            migrationBuilder.DropColumn(
                name: "CarriedFromYear",
                table: "PayslipLineItems");
        }
    }
}
