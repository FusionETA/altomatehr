using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class PayslipPdfPasswordRule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PdfPasswordRule",
                table: "Payslips",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        
            // A payslip that already has its own random password was locked
            // with RANDOM (only local/dev data: production had none).
            migrationBuilder.Sql(
                "UPDATE `Payslips` SET `PdfPasswordRule` = 'RANDOM' WHERE `PdfPasswordEncrypted` IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PdfPasswordRule",
                table: "Payslips");
        }
    }
}
