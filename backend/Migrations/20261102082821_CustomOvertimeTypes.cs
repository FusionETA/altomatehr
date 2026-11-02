using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class CustomOvertimeTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OtRatePublicHolidayInShift",
                table: "EmployeePolicies");

            migrationBuilder.DropColumn(
                name: "OtRateRestDayInShift",
                table: "EmployeePolicies");

            migrationBuilder.AddColumn<string>(
                name: "OtCustomJson",
                table: "Payslips",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "OtCustomHoursJson",
                table: "PayrollRunAdjustments",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "OvertimeTypeId",
                table: "OvertimeRequests",
                type: "varchar(40)",
                maxLength: 40,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<decimal>(
                name: "OtRateRestDay",
                table: "EmployeePolicies",
                type: "decimal(4,2)",
                precision: 4,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(4,2)",
                oldPrecision: 4,
                oldScale: 2);

            migrationBuilder.AlterColumn<decimal>(
                name: "OtRatePublicHoliday",
                table: "EmployeePolicies",
                type: "decimal(4,2)",
                precision: 4,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(4,2)",
                oldPrecision: 4,
                oldScale: 2);

            migrationBuilder.AlterColumn<decimal>(
                name: "OtRateNormalDay",
                table: "EmployeePolicies",
                type: "decimal(4,2)",
                precision: 4,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(4,2)",
                oldPrecision: 4,
                oldScale: 2);

            migrationBuilder.CreateTable(
                name: "OvertimeTypes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OrganizationId = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Kind = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Multiplier = table.Column<decimal>(type: "decimal(4,2)", precision: 4, scale: 2, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsArchived = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OvertimeTypes", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PolicyOvertimeRates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OrganizationId = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PolicyId = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OvertimeTypeId = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Multiplier = table.Column<decimal>(type: "decimal(4,2)", precision: 4, scale: 2, nullable: true),
                    Enabled = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolicyOvertimeRates", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_OvertimeTypes_OrganizationId_Kind",
                table: "OvertimeTypes",
                columns: new[] { "OrganizationId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyOvertimeRates_PolicyId_OvertimeTypeId",
                table: "PolicyOvertimeRates",
                columns: new[] { "PolicyId", "OvertimeTypeId" },
                unique: true);

            // ---- Data ----
            // Existing rows read as "no custom overtime".
            migrationBuilder.Sql("UPDATE `Payslips` SET `OtCustomJson` = '[]' WHERE `OtCustomJson` = '';");
            migrationBuilder.Sql("UPDATE `PayrollRunAdjustments` SET `OtCustomHoursJson` = '{}' WHERE `OtCustomHoursJson` = '';");

            // Every company gets the three built-in types at the statutory
            // multipliers (OvertimeTypeService.BuiltIns).
            migrationBuilder.Sql(@"
INSERT INTO `OvertimeTypes` (`Id`, `OrganizationId`, `Name`, `Kind`, `Multiplier`, `SortOrder`, `IsArchived`, `CreatedAt`, `UpdatedAt`)
SELECT UUID(), o.`Id`, b.`Name`, b.`Kind`, b.`Multiplier`, 0, 0, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)
FROM `Organizations` o
CROSS JOIN (
    SELECT 'Normal day' AS `Name`, 'NORMAL_DAY' AS `Kind`, 1.50 AS `Multiplier`
    UNION ALL SELECT 'Rest day', 'REST_DAY', 2.00
    UNION ALL SELECT 'Public holiday', 'PUBLIC_HOLIDAY', 3.00
) b;");

            // A policy rate equal to the company's now follows the company's;
            // one that differs stays as the policy's own, so no pay changes.
            migrationBuilder.Sql("UPDATE `EmployeePolicies` SET `OtRateNormalDay` = NULL WHERE `OtRateNormalDay` = 1.50;");
            migrationBuilder.Sql("UPDATE `EmployeePolicies` SET `OtRateRestDay` = NULL WHERE `OtRateRestDay` = 2.00;");
            migrationBuilder.Sql("UPDATE `EmployeePolicies` SET `OtRatePublicHoliday` = NULL WHERE `OtRatePublicHoliday` = 3.00;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Back to the per-policy rates: a policy following the company's
            // takes the company's current figure.
            migrationBuilder.Sql(@"
UPDATE `EmployeePolicies` p
LEFT JOIN `OvertimeTypes` n ON n.`OrganizationId` = p.`OrganizationId` AND n.`Kind` = 'NORMAL_DAY'
LEFT JOIN `OvertimeTypes` r ON r.`OrganizationId` = p.`OrganizationId` AND r.`Kind` = 'REST_DAY'
LEFT JOIN `OvertimeTypes` h ON h.`OrganizationId` = p.`OrganizationId` AND h.`Kind` = 'PUBLIC_HOLIDAY'
SET p.`OtRateNormalDay` = COALESCE(p.`OtRateNormalDay`, n.`Multiplier`, 1.50),
    p.`OtRateRestDay` = COALESCE(p.`OtRateRestDay`, r.`Multiplier`, 2.00),
    p.`OtRatePublicHoliday` = COALESCE(p.`OtRatePublicHoliday`, h.`Multiplier`, 3.00);");

            migrationBuilder.DropTable(
                name: "OvertimeTypes");

            migrationBuilder.DropTable(
                name: "PolicyOvertimeRates");

            migrationBuilder.DropColumn(
                name: "OtCustomJson",
                table: "Payslips");

            migrationBuilder.DropColumn(
                name: "OtCustomHoursJson",
                table: "PayrollRunAdjustments");

            migrationBuilder.DropColumn(
                name: "OvertimeTypeId",
                table: "OvertimeRequests");

            migrationBuilder.AlterColumn<decimal>(
                name: "OtRateRestDay",
                table: "EmployeePolicies",
                type: "decimal(4,2)",
                precision: 4,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(4,2)",
                oldPrecision: 4,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "OtRatePublicHoliday",
                table: "EmployeePolicies",
                type: "decimal(4,2)",
                precision: 4,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(4,2)",
                oldPrecision: 4,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "OtRateNormalDay",
                table: "EmployeePolicies",
                type: "decimal(4,2)",
                precision: 4,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(4,2)",
                oldPrecision: 4,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OtRatePublicHolidayInShift",
                table: "EmployeePolicies",
                type: "decimal(4,2)",
                precision: 4,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OtRateRestDayInShift",
                table: "EmployeePolicies",
                type: "decimal(4,2)",
                precision: 4,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }
    }
}
