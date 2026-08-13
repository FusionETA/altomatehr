using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddGeofencing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "Latitude",
                table: "Projects",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitude",
                table: "Projects",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GeofenceRadiusMeters",
                table: "Organizations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "ClockInDistanceMeters",
                table: "AttendanceRecords",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ClockInLat",
                table: "AttendanceRecords",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ClockInLng",
                table: "AttendanceRecords",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ClockOutDistanceMeters",
                table: "AttendanceRecords",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ClockOutLat",
                table: "AttendanceRecords",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ClockOutLng",
                table: "AttendanceRecords",
                type: "double",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "GeofenceRadiusMeters",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "ClockInDistanceMeters",
                table: "AttendanceRecords");

            migrationBuilder.DropColumn(
                name: "ClockInLat",
                table: "AttendanceRecords");

            migrationBuilder.DropColumn(
                name: "ClockInLng",
                table: "AttendanceRecords");

            migrationBuilder.DropColumn(
                name: "ClockOutDistanceMeters",
                table: "AttendanceRecords");

            migrationBuilder.DropColumn(
                name: "ClockOutLat",
                table: "AttendanceRecords");

            migrationBuilder.DropColumn(
                name: "ClockOutLng",
                table: "AttendanceRecords");
        }
    }
}
