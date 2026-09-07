using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddClaimXeroSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "XeroBillId",
                table: "Claims",
                type: "varchar(60)",
                maxLength: 60,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "XeroBillRef",
                table: "Claims",
                type: "varchar(60)",
                maxLength: 60,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "XeroSyncError",
                table: "Claims",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "XeroSyncStatus",
                table: "Claims",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "XeroSyncedAt",
                table: "Claims",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Claims_XeroBillId",
                table: "Claims",
                column: "XeroBillId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Claims_XeroBillId",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "XeroBillId",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "XeroBillRef",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "XeroSyncError",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "XeroSyncStatus",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "XeroSyncedAt",
                table: "Claims");
        }
    }
}
