using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLeaveEntitlements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccrualMethod",
                table: "PolicyLeaveEntitlements",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "AccrualMethod",
                table: "LeaveTypes",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                // Existing rows must land on a VALID enum name — the generated
                // default of "" cannot convert back to LeaveAccrualMethod.
                // LUMP_SUM is the pre-existing behaviour (full entitlement up front).
                defaultValue: "LUMP_SUM")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "CarryExpiryMonth",
                table: "LeaveTypes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CarryForward",
                table: "LeaveTypes",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<double>(
                name: "MaxCarryForwardDays",
                table: "LeaveTypes",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ProrateFirstYear",
                table: "LeaveTypes",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "XeroFileId",
                table: "LeaveApplications",
                type: "varchar(80)",
                maxLength: 80,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LeaveEntitlements",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OrganizationId = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EmployeeId = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LeaveTypeId = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Year = table.Column<int>(type: "int", nullable: false),
                    EntitledDays = table.Column<double>(type: "double", nullable: false),
                    AccruedDays = table.Column<double>(type: "double", nullable: false),
                    CarriedDays = table.Column<double>(type: "double", nullable: false),
                    CarriedExpiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CarriedExpired = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CarriedExpiredAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CarriedExpiredDays = table.Column<double>(type: "double", nullable: true),
                    AccrualMethod = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveEntitlements", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveEntitlements_CarriedExpired_CarriedExpiresAt",
                table: "LeaveEntitlements",
                columns: new[] { "CarriedExpired", "CarriedExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveEntitlements_OrganizationId_EmployeeId_LeaveTypeId_Year",
                table: "LeaveEntitlements",
                columns: new[] { "OrganizationId", "EmployeeId", "LeaveTypeId", "Year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaveEntitlements_Year",
                table: "LeaveEntitlements",
                column: "Year");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeaveEntitlements");

            migrationBuilder.DropColumn(
                name: "AccrualMethod",
                table: "PolicyLeaveEntitlements");

            migrationBuilder.DropColumn(
                name: "AccrualMethod",
                table: "LeaveTypes");

            migrationBuilder.DropColumn(
                name: "CarryExpiryMonth",
                table: "LeaveTypes");

            migrationBuilder.DropColumn(
                name: "CarryForward",
                table: "LeaveTypes");

            migrationBuilder.DropColumn(
                name: "MaxCarryForwardDays",
                table: "LeaveTypes");

            migrationBuilder.DropColumn(
                name: "ProrateFirstYear",
                table: "LeaveTypes");

            migrationBuilder.DropColumn(
                name: "XeroFileId",
                table: "LeaveApplications");
        }
    }
}
