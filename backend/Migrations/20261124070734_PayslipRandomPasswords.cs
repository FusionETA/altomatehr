using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class PayslipRandomPasswords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The shared COMPANY rule is gone; a row still on it would no
            // longer parse. Move it to RANDOM, the closest rule (a password the
            // employee can't derive). Production had none on 2026-11-24.
            migrationBuilder.Sql(
                "UPDATE `PayrollSettings` SET `PayslipPasswordRule` = 'RANDOM' WHERE `PayslipPasswordRule` = 'COMPANY';");

            migrationBuilder.DropColumn(
                name: "PayslipCompanyPasswordEncrypted",
                table: "PayrollSettings");

            migrationBuilder.AddColumn<string>(
                name: "PdfPasswordEncrypted",
                table: "Payslips",
                type: "varchar(512)",
                maxLength: 512,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PdfPasswordEncrypted",
                table: "Payslips");

            migrationBuilder.AddColumn<string>(
                name: "PayslipCompanyPasswordEncrypted",
                table: "PayrollSettings",
                type: "varchar(512)",
                maxLength: 512,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }
    }
}
