using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class EmployeeProfileWorkPermit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "WorkPermitExpiry",
                table: "EmployeeProfiles",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkPermitNumber",
                table: "EmployeeProfiles",
                type: "varchar(40)",
                maxLength: 40,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WorkPermitExpiry",
                table: "EmployeeProfiles");

            migrationBuilder.DropColumn(
                name: "WorkPermitNumber",
                table: "EmployeeProfiles");
        }
    }
}
