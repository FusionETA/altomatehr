using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddIpWhitelistAndGpsCaptureFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AllowedIps",
                table: "Projects",
                type: "varchar(1000)",
                maxLength: 1000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "CaptureLocationOnClockIn",
                table: "EmployeePolicies",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "CaptureLocationOnClockOut",
                table: "EmployeePolicies",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "GeolocationEnabled",
                table: "EmployeePolicies",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequireIpWhitelist",
                table: "EmployeePolicies",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowedIps",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "CaptureLocationOnClockIn",
                table: "EmployeePolicies");

            migrationBuilder.DropColumn(
                name: "CaptureLocationOnClockOut",
                table: "EmployeePolicies");

            migrationBuilder.DropColumn(
                name: "GeolocationEnabled",
                table: "EmployeePolicies");

            migrationBuilder.DropColumn(
                name: "RequireIpWhitelist",
                table: "EmployeePolicies");
        }
    }
}
