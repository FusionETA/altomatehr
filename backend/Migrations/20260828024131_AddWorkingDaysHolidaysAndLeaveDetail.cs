using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkingDaysHolidaysAndLeaveDetail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.AddColumn<string>(
                name: "WorkingDays",
                table: "Organizations",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "JoinDate",
                table: "OrganizationMemberships",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AppliedByAdminId",
                table: "LeaveApplications",
                type: "varchar(40)",
                maxLength: 40,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Approvals",
                table: "LeaveApplications",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "AttachmentName",
                table: "LeaveApplications",
                type: "varchar(260)",
                maxLength: 260,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Duration",
                table: "LeaveApplications",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                // Existing rows need a VALID enum name — the generated "" will
                // not convert back to LeaveDuration. Every request created so
                // far is a full day, which is also the prior behaviour.
                defaultValue: "FULL_DAY")
                .Annotation("MySql:CharSet", "utf8mb4");


        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {


            migrationBuilder.DropColumn(
                name: "WorkingDays",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "JoinDate",
                table: "OrganizationMemberships");

            migrationBuilder.DropColumn(
                name: "AppliedByAdminId",
                table: "LeaveApplications");

            migrationBuilder.DropColumn(
                name: "Approvals",
                table: "LeaveApplications");

            migrationBuilder.DropColumn(
                name: "AttachmentName",
                table: "LeaveApplications");

            migrationBuilder.DropColumn(
                name: "Duration",
                table: "LeaveApplications");
        }
    }
}
