using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class PerPersonPerMonth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PeriodMonth",
                table: "Payslips",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PeriodYear",
                table: "Payslips",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PeriodMonth",
                table: "PayrollRunMembers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PeriodYear",
                table: "PayrollRunMembers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Copy each run's month onto its payslips and roster rows BEFORE the
            // unique keys go on, or every existing row would read as month 0.
            // Safe today: a month has one run, so no person has two rows in it.
            // Roster rows of runs that were deleted (deleting a run didn't clear
            // its roster until now). They have no month to copy and would all
            // collide on month 0, so they go first.
            migrationBuilder.Sql(
                "DELETE m FROM PayrollRunMembers m LEFT JOIN PayrollRuns r ON r.Id = m.PayrollRunId " +
                "WHERE r.Id IS NULL;");
            migrationBuilder.Sql(
                "UPDATE Payslips p JOIN PayrollRuns r ON r.Id = p.PayrollRunId " +
                "SET p.PeriodYear = r.PeriodYear, p.PeriodMonth = r.PeriodMonth;");
            migrationBuilder.Sql(
                "UPDATE PayrollRunMembers m JOIN PayrollRuns r ON r.Id = m.PayrollRunId " +
                "SET m.PeriodYear = r.PeriodYear, m.PeriodMonth = r.PeriodMonth;");

            migrationBuilder.CreateIndex(
                name: "IX_Payslips_OrganizationId_PeriodYear_PeriodMonth_EmployeeProfi~",
                table: "Payslips",
                columns: new[] { "OrganizationId", "PeriodYear", "PeriodMonth", "EmployeeProfileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRunMembers_OrganizationId_PeriodYear_PeriodMonth_Empl~",
                table: "PayrollRunMembers",
                columns: new[] { "OrganizationId", "PeriodYear", "PeriodMonth", "EmployeeProfileId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Payslips_OrganizationId_PeriodYear_PeriodMonth_EmployeeProfi~",
                table: "Payslips");

            migrationBuilder.DropIndex(
                name: "IX_PayrollRunMembers_OrganizationId_PeriodYear_PeriodMonth_Empl~",
                table: "PayrollRunMembers");

            migrationBuilder.DropColumn(
                name: "PeriodMonth",
                table: "Payslips");

            migrationBuilder.DropColumn(
                name: "PeriodYear",
                table: "Payslips");

            migrationBuilder.DropColumn(
                name: "PeriodMonth",
                table: "PayrollRunMembers");

            migrationBuilder.DropColumn(
                name: "PeriodYear",
                table: "PayrollRunMembers");
        }
    }
}
