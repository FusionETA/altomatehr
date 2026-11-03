using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class RunTotalPcbBorneByEmployer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "TotalPcbBorneByEmployer",
                table: "PayrollRuns",
                type: "decimal(14,2)",
                precision: 14,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            // Runs generated since Payslips.PcbBorneByEmployer arrived already
            // carry it per payslip; without this their run total stays 0 and
            // the run's gross keeps showing that PCB (filed runs can't be
            // regenerated to fix it).
            migrationBuilder.Sql(
                "UPDATE PayrollRuns r SET r.TotalPcbBorneByEmployer = COALESCE(("
                + "SELECT SUM(p.PcbBorneByEmployer) FROM Payslips p WHERE p.PayrollRunId = r.Id), 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TotalPcbBorneByEmployer",
                table: "PayrollRuns");
        }
    }
}
