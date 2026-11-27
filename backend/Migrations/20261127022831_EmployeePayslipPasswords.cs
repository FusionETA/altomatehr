using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class EmployeePayslipPasswords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // One payslip password per person now (EmployeeProfiles), instead
            // of one per payslip. Added first, so the passwords already handed
            // out can be carried over before their columns go.
            migrationBuilder.AddColumn<string>(
                name: "PayslipPasswordEncrypted",
                table: "EmployeeProfiles",
                type: "varchar(512)",
                maxLength: 512,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            // Someone who already has RANDOM-locked payslips keeps their latest
            // one's password as theirs, so the newest file they hold still
            // opens. (Production: one person, two payslips, at this point.)
            migrationBuilder.Sql(
                """
                UPDATE `EmployeeProfiles` ep
                JOIN (
                    SELECT `EmployeeProfileId`, `PdfPasswordEncrypted`,
                           ROW_NUMBER() OVER (PARTITION BY `EmployeeProfileId`
                                              ORDER BY `PeriodYear` DESC, `PeriodMonth` DESC) AS rn
                    FROM `Payslips`
                    WHERE `PdfPasswordRule` = 'RANDOM' AND `PdfPasswordEncrypted` IS NOT NULL
                ) latest ON latest.`EmployeeProfileId` = ep.`Id` AND latest.rn = 1
                SET ep.`PayslipPasswordEncrypted` = latest.`PdfPasswordEncrypted`;
                """);

            migrationBuilder.DropColumn(
                name: "PdfPasswordEncrypted",
                table: "Payslips");

            migrationBuilder.DropColumn(
                name: "PdfPasswordRule",
                table: "Payslips");

            // Every company that locks payslips now locks them with each
            // employee's own password; there is no rule to choose.
            migrationBuilder.DropColumn(
                name: "PayslipPasswordRule",
                table: "PayrollSettings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PayslipPasswordEncrypted",
                table: "EmployeeProfiles");

            migrationBuilder.AddColumn<string>(
                name: "PdfPasswordEncrypted",
                table: "Payslips",
                type: "varchar(512)",
                maxLength: 512,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "PdfPasswordRule",
                table: "Payslips",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "PayslipPasswordRule",
                table: "PayrollSettings",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "IC_DIGITS")
                .Annotation("MySql:CharSet", "utf8mb4");

            // The per-person passwords aren't copied back: a company that locks
            // goes back to RANDOM, and its payslips get new ones on next lock.
            migrationBuilder.Sql(
                "UPDATE `PayrollSettings` SET `PayslipPasswordRule` = 'RANDOM' WHERE `LockPayslips` = 1;");
        }
    }
}
