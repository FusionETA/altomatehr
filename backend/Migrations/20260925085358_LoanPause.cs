using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class LoanPause : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PausedFromMonth",
                table: "EmployeeLoans",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PausedFromYear",
                table: "EmployeeLoans",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PausedFromMonth",
                table: "EmployeeLoans");

            migrationBuilder.DropColumn(
                name: "PausedFromYear",
                table: "EmployeeLoans");
        }
    }
}
