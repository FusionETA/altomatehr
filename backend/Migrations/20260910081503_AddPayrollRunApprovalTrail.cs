using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollRunApprovalTrail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApprovalRejectionReason",
                table: "PayrollRuns",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedAt",
                table: "PayrollRuns",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubmittedById",
                table: "PayrollRuns",
                type: "varchar(40)",
                maxLength: 40,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedForApprovalAt",
                table: "PayrollRuns",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubmittedForApprovalById",
                table: "PayrollRuns",
                type: "varchar(40)",
                maxLength: 40,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApprovalRejectionReason",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "SubmittedAt",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "SubmittedById",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "SubmittedForApprovalAt",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "SubmittedForApprovalById",
                table: "PayrollRuns");
        }
    }
}
