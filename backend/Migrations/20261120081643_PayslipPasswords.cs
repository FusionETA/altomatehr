using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class PayslipPasswords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "LockPayslips",
                table: "PayrollSettings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PayslipPasswordRule",
                table: "PayrollSettings",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                // Existing companies read back as a real rule, not "" (which
                // no enum value matches). LockPayslips defaults off anyway.
                defaultValue: "IC_DIGITS")
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LockPayslips",
                table: "PayrollSettings");

            migrationBuilder.DropColumn(
                name: "PayslipPasswordRule",
                table: "PayrollSettings");
        }
    }
}
