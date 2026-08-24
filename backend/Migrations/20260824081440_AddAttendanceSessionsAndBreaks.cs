using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceSessionsAndBreaks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CaptureLocationOnBreakEnd",
                table: "EmployeePolicies",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "CaptureLocationOnBreakStart",
                table: "EmployeePolicies",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "AttendanceBreaks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OrganizationId = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AttendanceSessionId = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AttendanceRecordId = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EmployeeId = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StartedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    StartLat = table.Column<double>(type: "double", nullable: true),
                    StartLng = table.Column<double>(type: "double", nullable: true),
                    EndLat = table.Column<double>(type: "double", nullable: true),
                    EndLng = table.Column<double>(type: "double", nullable: true),
                    Remark = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ApprovalStatus = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CurrentStep = table.Column<int>(type: "int", nullable: false),
                    ReviewNotes = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SubmittedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceBreaks", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AttendanceSessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OrganizationId = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AttendanceRecordId = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EmployeeId = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StartedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceSessions", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceBreaks_ApprovalStatus_EmployeeId",
                table: "AttendanceBreaks",
                columns: new[] { "ApprovalStatus", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceBreaks_AttendanceRecordId",
                table: "AttendanceBreaks",
                column: "AttendanceRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceBreaks_AttendanceSessionId",
                table: "AttendanceBreaks",
                column: "AttendanceSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceBreaks_AttendanceSessionId_EndedAt",
                table: "AttendanceBreaks",
                columns: new[] { "AttendanceSessionId", "EndedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceSessions_AttendanceRecordId",
                table: "AttendanceSessions",
                column: "AttendanceRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceSessions_AttendanceRecordId_EndedAt",
                table: "AttendanceSessions",
                columns: new[] { "AttendanceRecordId", "EndedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceBreaks");

            migrationBuilder.DropTable(
                name: "AttendanceSessions");

            migrationBuilder.DropColumn(
                name: "CaptureLocationOnBreakEnd",
                table: "EmployeePolicies");

            migrationBuilder.DropColumn(
                name: "CaptureLocationOnBreakStart",
                table: "EmployeePolicies");
        }
    }
}
