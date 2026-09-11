using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollRunXeroSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "XeroJournalNumber",
                table: "PayrollRuns",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "XeroManualJournalId",
                table: "PayrollRuns",
                type: "varchar(80)",
                maxLength: 80,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "XeroSyncError",
                table: "PayrollRuns",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "XeroSyncStatus",
                table: "PayrollRuns",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "XeroSyncedAt",
                table: "PayrollRuns",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_XeroManualJournalId",
                table: "PayrollRuns",
                column: "XeroManualJournalId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PayrollRuns_XeroManualJournalId",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "XeroJournalNumber",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "XeroManualJournalId",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "XeroSyncError",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "XeroSyncStatus",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "XeroSyncedAt",
                table: "PayrollRuns");
        }
    }
}
