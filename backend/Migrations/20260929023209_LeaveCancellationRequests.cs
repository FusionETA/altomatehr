using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class LeaveCancellationRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "LeaveApplications",
                type: "varchar(1000)",
                maxLength: 1000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "CancellationRequestedAt",
                table: "LeaveApplications",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancellationStatus",
                table: "LeaveApplications",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "CancellationStep",
                table: "LeaveApplications",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "LeaveApplications");

            migrationBuilder.DropColumn(
                name: "CancellationRequestedAt",
                table: "LeaveApplications");

            migrationBuilder.DropColumn(
                name: "CancellationStatus",
                table: "LeaveApplications");

            migrationBuilder.DropColumn(
                name: "CancellationStep",
                table: "LeaveApplications");
        }
    }
}
