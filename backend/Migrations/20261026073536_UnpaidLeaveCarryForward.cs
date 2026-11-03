using System;
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
            migrationBuilder.AddColumn<bool>(
                name: "TracksUnpaidLeave",
                table: "Payslips",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

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

            migrationBuilder.CreateTable(
                name: "UnpaidLeaveSettlements",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OrganizationId = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PayrollRunId = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PayslipId = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EmployeeProfileId = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LeaveApplicationId = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PeriodYear = table.Column<int>(type: "int", nullable: false),
                    PeriodMonth = table.Column<int>(type: "int", nullable: false),
                    Days = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: false),
                    LeaveTypeId = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LeaveStartDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LeaveEndDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Final = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnpaidLeaveSettlements", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_UnpaidLeaveSettlements_OrganizationId_PeriodYear",
                table: "UnpaidLeaveSettlements",
                columns: new[] { "OrganizationId", "PeriodYear" });

            migrationBuilder.CreateIndex(
                name: "IX_UnpaidLeaveSettlements_PayrollRunId",
                table: "UnpaidLeaveSettlements",
                column: "PayrollRunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UnpaidLeaveSettlements");

            migrationBuilder.DropColumn(
                name: "TracksUnpaidLeave",
                table: "Payslips");

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
