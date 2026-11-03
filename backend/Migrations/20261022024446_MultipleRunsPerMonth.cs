using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class MultipleRunsPerMonth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PayrollRuns_OrganizationId_PeriodYear_PeriodMonth",
                table: "PayrollRuns");

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "PayrollRuns",
                type: "varchar(60)",
                maxLength: 60,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "RunNumber",
                table: "PayrollRuns",
                type: "int",
                nullable: false,
                defaultValue: 1);   // every existing run is its month's first

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_OrganizationId_PeriodYear_PeriodMonth",
                table: "PayrollRuns",
                columns: new[] { "OrganizationId", "PeriodYear", "PeriodMonth" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PayrollRuns_OrganizationId_PeriodYear_PeriodMonth",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "RunNumber",
                table: "PayrollRuns");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_OrganizationId_PeriodYear_PeriodMonth",
                table: "PayrollRuns",
                columns: new[] { "OrganizationId", "PeriodYear", "PeriodMonth" },
                unique: true);
        }
    }
}
