using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAbPayCompanyCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AbPayCompanyCode",
                table: "PayrollSettings",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AbPayCompanyCode",
                table: "PayrollSettings");
        }
    }
}
