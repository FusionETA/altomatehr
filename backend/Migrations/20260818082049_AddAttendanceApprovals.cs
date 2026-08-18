using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApprovalStatus",
                table: "AttendanceRecords",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "PENDING")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "CurrentStep",
                table: "AttendanceRecords",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "DecidedAt",
                table: "AttendanceRecords",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewNotes",
                table: "AttendanceRecords",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedAt",
                table: "AttendanceRecords",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_ApprovalStatus_Date",
                table: "AttendanceRecords",
                columns: new[] { "ApprovalStatus", "Date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AttendanceRecords_ApprovalStatus_Date",
                table: "AttendanceRecords");

            migrationBuilder.DropColumn(
                name: "ApprovalStatus",
                table: "AttendanceRecords");

            migrationBuilder.DropColumn(
                name: "CurrentStep",
                table: "AttendanceRecords");

            migrationBuilder.DropColumn(
                name: "DecidedAt",
                table: "AttendanceRecords");

            migrationBuilder.DropColumn(
                name: "ReviewNotes",
                table: "AttendanceRecords");

            migrationBuilder.DropColumn(
                name: "SubmittedAt",
                table: "AttendanceRecords");
        }
    }
}
