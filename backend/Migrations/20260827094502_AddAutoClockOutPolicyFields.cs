using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAutoClockOutPolicyFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AutoClockOutAfterMinutes",
                table: "EmployeePolicies",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AutoClockOutEnabled",
                table: "EmployeePolicies",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoClockOutAfterMinutes",
                table: "EmployeePolicies");

            migrationBuilder.DropColumn(
                name: "AutoClockOutEnabled",
                table: "EmployeePolicies");
        }
    }
}
