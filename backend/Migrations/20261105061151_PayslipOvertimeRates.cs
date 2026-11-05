using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class PayslipOvertimeRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "OtHourlyRate",
                table: "Payslips",
                type: "decimal(14,6)",
                precision: 14,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OtRateNormal",
                table: "Payslips",
                type: "decimal(4,2)",
                precision: 4,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OtRatePublicHoliday",
                table: "Payslips",
                type: "decimal(4,2)",
                precision: 4,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OtRateRest",
                table: "Payslips",
                type: "decimal(4,2)",
                precision: 4,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OtHourlyRate",
                table: "Payslips");

            migrationBuilder.DropColumn(
                name: "OtRateNormal",
                table: "Payslips");

            migrationBuilder.DropColumn(
                name: "OtRatePublicHoliday",
                table: "Payslips");

            migrationBuilder.DropColumn(
                name: "OtRateRest",
                table: "Payslips");
        }
    }
}
