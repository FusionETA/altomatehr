using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class MultiShiftSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "ClockInDistanceMeters",
                table: "AttendanceSessions",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ClockInLat",
                table: "AttendanceSessions",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ClockInLng",
                table: "AttendanceSessions",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClockInPhotoUrl",
                table: "AttendanceSessions",
                type: "varchar(400)",
                maxLength: 400,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<double>(
                name: "ClockOutDistanceMeters",
                table: "AttendanceSessions",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ClockOutLat",
                table: "AttendanceSessions",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ClockOutLng",
                table: "AttendanceSessions",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClockOutPhotoUrl",
                table: "AttendanceSessions",
                type: "varchar(400)",
                maxLength: 400,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "DurationMin",
                table: "AttendanceSessions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LateByMin",
                table: "AttendanceSessions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "AttendanceSessions",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");
        
            // Existing sessions were hollow mirrors of their record. Copy the
            // day's clock evidence down into them so history predating
            // multi-shift still has a session that describes itself — every one
            // of those days had exactly one session, so record and session say
            // the same thing by construction.
            migrationBuilder.Sql(@"
                UPDATE AttendanceSessions s
                JOIN AttendanceRecords r ON r.Id = s.AttendanceRecordId
                SET s.DurationMin           = r.DurationMin,
                    s.Status                = r.Status,
                    s.LateByMin             = r.LateByMin,
                    s.ClockInLat            = r.ClockInLat,
                    s.ClockInLng            = r.ClockInLng,
                    s.ClockInDistanceMeters = r.ClockInDistanceMeters,
                    s.ClockInPhotoUrl       = r.ClockInPhotoUrl,
                    s.ClockOutLat           = r.ClockOutLat,
                    s.ClockOutLng           = r.ClockOutLng,
                    s.ClockOutDistanceMeters= r.ClockOutDistanceMeters,
                    s.ClockOutPhotoUrl      = r.ClockOutPhotoUrl;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClockInDistanceMeters",
                table: "AttendanceSessions");

            migrationBuilder.DropColumn(
                name: "ClockInLat",
                table: "AttendanceSessions");

            migrationBuilder.DropColumn(
                name: "ClockInLng",
                table: "AttendanceSessions");

            migrationBuilder.DropColumn(
                name: "ClockInPhotoUrl",
                table: "AttendanceSessions");

            migrationBuilder.DropColumn(
                name: "ClockOutDistanceMeters",
                table: "AttendanceSessions");

            migrationBuilder.DropColumn(
                name: "ClockOutLat",
                table: "AttendanceSessions");

            migrationBuilder.DropColumn(
                name: "ClockOutLng",
                table: "AttendanceSessions");

            migrationBuilder.DropColumn(
                name: "ClockOutPhotoUrl",
                table: "AttendanceSessions");

            migrationBuilder.DropColumn(
                name: "DurationMin",
                table: "AttendanceSessions");

            migrationBuilder.DropColumn(
                name: "LateByMin",
                table: "AttendanceSessions");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "AttendanceSessions");
        }
    }
}
