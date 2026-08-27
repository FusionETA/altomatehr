using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddOtRatesAndHolidays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "OtRateNormalDay",
                table: "EmployeePolicies",
                type: "decimal(4,2)",
                precision: 4,
                scale: 2,
                nullable: false,
                defaultValue: 1.50m);

            migrationBuilder.AddColumn<decimal>(
                name: "OtRatePublicHoliday",
                table: "EmployeePolicies",
                type: "decimal(4,2)",
                precision: 4,
                scale: 2,
                nullable: false,
                defaultValue: 3.00m);

            migrationBuilder.AddColumn<decimal>(
                name: "OtRatePublicHolidayInShift",
                table: "EmployeePolicies",
                type: "decimal(4,2)",
                precision: 4,
                scale: 2,
                nullable: false,
                defaultValue: 2.00m);

            migrationBuilder.AddColumn<decimal>(
                name: "OtRateRestDay",
                table: "EmployeePolicies",
                type: "decimal(4,2)",
                precision: 4,
                scale: 2,
                nullable: false,
                defaultValue: 2.00m);

            migrationBuilder.AddColumn<decimal>(
                name: "OtRateRestDayInShift",
                table: "EmployeePolicies",
                type: "decimal(4,2)",
                precision: 4,
                scale: 2,
                nullable: false,
                defaultValue: 1.00m);

            migrationBuilder.AddColumn<decimal>(
                name: "OtSalaryThreshold",
                table: "EmployeePolicies",
                type: "decimal(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Holidays",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OrganizationId = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ProjectId = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Date = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Name = table.Column<string>(type: "varchar(160)", maxLength: 160, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Holidays", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Holidays_Date_ProjectId",
                table: "Holidays",
                columns: new[] { "Date", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_Holidays_ProjectId",
                table: "Holidays",
                column: "ProjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Holidays");

            migrationBuilder.DropColumn(
                name: "OtRateNormalDay",
                table: "EmployeePolicies");

            migrationBuilder.DropColumn(
                name: "OtRatePublicHoliday",
                table: "EmployeePolicies");

            migrationBuilder.DropColumn(
                name: "OtRatePublicHolidayInShift",
                table: "EmployeePolicies");

            migrationBuilder.DropColumn(
                name: "OtRateRestDay",
                table: "EmployeePolicies");

            migrationBuilder.DropColumn(
                name: "OtRateRestDayInShift",
                table: "EmployeePolicies");

            migrationBuilder.DropColumn(
                name: "OtSalaryThreshold",
                table: "EmployeePolicies");
        }
    }
}
