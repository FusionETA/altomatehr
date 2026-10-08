using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class PayrollStartMonth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PayrollStartMonth",
                table: "PayrollSettings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PayrollStartYear",
                table: "PayrollSettings",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PayrollStartMonth",
                table: "PayrollSettings");

            migrationBuilder.DropColumn(
                name: "PayrollStartYear",
                table: "PayrollSettings");
        }
    }
}
