using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class SpecialTaxScheme : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SpecialTaxFrom",
                table: "EmployeeProfiles",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpecialTaxScheme",
                table: "EmployeeProfiles",
                type: "varchar(30)",
                maxLength: 30,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "SpecialTaxTo",
                table: "EmployeeProfiles",
                type: "datetime(6)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SpecialTaxFrom",
                table: "EmployeeProfiles");

            migrationBuilder.DropColumn(
                name: "SpecialTaxScheme",
                table: "EmployeeProfiles");

            migrationBuilder.DropColumn(
                name: "SpecialTaxTo",
                table: "EmployeeProfiles");
        }
    }
}
