using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class PayrollRunLhdnReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "Cp38ReceiptDate",
                table: "PayrollRuns",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Cp38ReceiptNo",
                table: "PayrollRuns",
                type: "varchar(60)",
                maxLength: 60,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "PcbReceiptDate",
                table: "PayrollRuns",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PcbReceiptNo",
                table: "PayrollRuns",
                type: "varchar(60)",
                maxLength: 60,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Cp38ReceiptDate",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "Cp38ReceiptNo",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "PcbReceiptDate",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "PcbReceiptNo",
                table: "PayrollRuns");
        }
    }
}
