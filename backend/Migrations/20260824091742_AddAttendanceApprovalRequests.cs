using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceApprovalRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AttendanceApprovalRequests",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OrganizationId = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EmployeeId = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Kind = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AttendanceRecordId = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AttendanceSessionId = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AttendanceBreakId = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EventAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ApprovalStatus = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CurrentStep = table.Column<int>(type: "int", nullable: false),
                    ReviewNotes = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReviewerId = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SubmittedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceApprovalRequests", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceApprovalRequests_ApprovalStatus_Kind",
                table: "AttendanceApprovalRequests",
                columns: new[] { "ApprovalStatus", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceApprovalRequests_AttendanceBreakId",
                table: "AttendanceApprovalRequests",
                column: "AttendanceBreakId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceApprovalRequests_AttendanceRecordId",
                table: "AttendanceApprovalRequests",
                column: "AttendanceRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceApprovalRequests_EmployeeId_SubmittedAt",
                table: "AttendanceApprovalRequests",
                columns: new[] { "EmployeeId", "SubmittedAt" });

            // ---- Backfill: one AttendanceApprovalRequest per existing record/break,
            // reconstructed from the columns about to be dropped below. Known,
            // accepted limitation: a record's CLOCK_IN decision, if it was later
            // clocked out, is not recoverable — that history was already lost by
            // the pre-existing overwrite bug before this migration ever ran. This
            // backfill only recovers the single surviving (most recent) decision
            // per record/break. ReviewerId is left NULL — who decided was never
            // stored before this migration.
            migrationBuilder.Sql(@"
                INSERT INTO AttendanceApprovalRequests
                  (Id, OrganizationId, EmployeeId, Kind, AttendanceRecordId, AttendanceSessionId, AttendanceBreakId,
                   EventAt, ApprovalStatus, CurrentStep, ReviewNotes, ReviewerId, SubmittedAt, DecidedAt, CreatedAt, UpdatedAt)
                SELECT
                  UUID(), r.OrganizationId, r.EmployeeId,
                  CASE WHEN r.TimeOut IS NOT NULL THEN 'CLOCK_OUT' ELSE 'CLOCK_IN' END,
                  r.Id,
                  (SELECT s.Id FROM AttendanceSessions s
                     WHERE s.AttendanceRecordId = r.Id ORDER BY s.StartedAt DESC LIMIT 1),
                  NULL,
                  COALESCE(r.TimeOut, r.TimeIn),
                  r.ApprovalStatus, r.CurrentStep, r.ReviewNotes, NULL,
                  COALESCE(r.SubmittedAt, r.TimeIn, r.CreatedAt),
                  r.DecidedAt, NOW(6), NOW(6)
                FROM AttendanceRecords r
                WHERE r.TimeIn IS NOT NULL;
            ");

            migrationBuilder.Sql(@"
                INSERT INTO AttendanceApprovalRequests
                  (Id, OrganizationId, EmployeeId, Kind, AttendanceRecordId, AttendanceSessionId, AttendanceBreakId,
                   EventAt, ApprovalStatus, CurrentStep, ReviewNotes, ReviewerId, SubmittedAt, DecidedAt, CreatedAt, UpdatedAt)
                SELECT
                  UUID(), b.OrganizationId, b.EmployeeId,
                  CASE WHEN b.EndedAt IS NOT NULL THEN 'BREAK_END' ELSE 'BREAK_START' END,
                  b.AttendanceRecordId, b.AttendanceSessionId, b.Id,
                  COALESCE(b.EndedAt, b.StartedAt),
                  b.ApprovalStatus, b.CurrentStep, b.ReviewNotes, NULL,
                  COALESCE(b.SubmittedAt, b.StartedAt, b.CreatedAt),
                  b.DecidedAt, NOW(6), NOW(6)
                FROM AttendanceBreaks b;
            ");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceRecords_ApprovalStatus_Date",
                table: "AttendanceRecords");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceBreaks_ApprovalStatus_EmployeeId",
                table: "AttendanceBreaks");

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

            migrationBuilder.DropColumn(
                name: "ApprovalStatus",
                table: "AttendanceBreaks");

            migrationBuilder.DropColumn(
                name: "CurrentStep",
                table: "AttendanceBreaks");

            migrationBuilder.DropColumn(
                name: "DecidedAt",
                table: "AttendanceBreaks");

            migrationBuilder.DropColumn(
                name: "ReviewNotes",
                table: "AttendanceBreaks");

            migrationBuilder.DropColumn(
                name: "SubmittedAt",
                table: "AttendanceBreaks");
        }

        /// <inheritdoc />
        // Best-effort/lossy — NOT a real rollback. Recovers only the most-recently-
        // submitted request per record/break; all independent per-event history
        // gained since this migration ran (the whole point of it) is discarded.
        // Acceptable as a local-dev safety net, not a production rollback path.
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApprovalStatus",
                table: "AttendanceRecords",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "")
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

            migrationBuilder.AddColumn<string>(
                name: "ApprovalStatus",
                table: "AttendanceBreaks",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "CurrentStep",
                table: "AttendanceBreaks",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "DecidedAt",
                table: "AttendanceBreaks",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewNotes",
                table: "AttendanceBreaks",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedAt",
                table: "AttendanceBreaks",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE AttendanceRecords r
                JOIN (
                    SELECT a1.*
                    FROM AttendanceApprovalRequests a1
                    JOIN (
                        SELECT AttendanceRecordId, MAX(SubmittedAt) AS MaxSubmittedAt
                        FROM AttendanceApprovalRequests
                        GROUP BY AttendanceRecordId
                    ) a2 ON a1.AttendanceRecordId = a2.AttendanceRecordId AND a1.SubmittedAt = a2.MaxSubmittedAt
                ) latest ON latest.AttendanceRecordId = r.Id
                SET r.ApprovalStatus = latest.ApprovalStatus,
                    r.CurrentStep = latest.CurrentStep,
                    r.ReviewNotes = latest.ReviewNotes,
                    r.SubmittedAt = latest.SubmittedAt,
                    r.DecidedAt = latest.DecidedAt;
            ");

            migrationBuilder.Sql(@"
                UPDATE AttendanceBreaks b
                JOIN (
                    SELECT a1.*
                    FROM AttendanceApprovalRequests a1
                    JOIN (
                        SELECT AttendanceBreakId, MAX(SubmittedAt) AS MaxSubmittedAt
                        FROM AttendanceApprovalRequests
                        WHERE AttendanceBreakId IS NOT NULL
                        GROUP BY AttendanceBreakId
                    ) a2 ON a1.AttendanceBreakId = a2.AttendanceBreakId AND a1.SubmittedAt = a2.MaxSubmittedAt
                ) latest ON latest.AttendanceBreakId = b.Id
                SET b.ApprovalStatus = latest.ApprovalStatus,
                    b.CurrentStep = latest.CurrentStep,
                    b.ReviewNotes = latest.ReviewNotes,
                    b.SubmittedAt = latest.SubmittedAt,
                    b.DecidedAt = latest.DecidedAt;
            ");

            migrationBuilder.DropTable(
                name: "AttendanceApprovalRequests");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_ApprovalStatus_Date",
                table: "AttendanceRecords",
                columns: new[] { "ApprovalStatus", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceBreaks_ApprovalStatus_EmployeeId",
                table: "AttendanceBreaks",
                columns: new[] { "ApprovalStatus", "EmployeeId" });
        }
    }
}
