using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class UniqueRunNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PayrollRuns_OrganizationId_PeriodYear_PeriodMonth",
                table: "PayrollRuns");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_OrganizationId_PeriodYear_PeriodMonth_RunNumber",
                table: "PayrollRuns",
                columns: new[] { "OrganizationId", "PeriodYear", "PeriodMonth", "RunNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PayrollRuns_OrganizationId_PeriodYear_PeriodMonth_RunNumber",
                table: "PayrollRuns");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_OrganizationId_PeriodYear_PeriodMonth",
                table: "PayrollRuns",
                columns: new[] { "OrganizationId", "PeriodYear", "PeriodMonth" });
        }
    }
}
