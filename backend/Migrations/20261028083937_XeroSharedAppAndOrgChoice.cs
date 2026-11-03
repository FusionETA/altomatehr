using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class XeroSharedAppAndOrgChoice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PendingChoiceExpiresAt",
                table: "XeroOAuthStates",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PendingChoiceProtected",
                table: "XeroOAuthStates",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "UsedByAnotherProduct",
                table: "XeroApps",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PendingChoiceExpiresAt",
                table: "XeroOAuthStates");

            migrationBuilder.DropColumn(
                name: "PendingChoiceProtected",
                table: "XeroOAuthStates");

            migrationBuilder.DropColumn(
                name: "UsedByAnotherProduct",
                table: "XeroApps");
        }
    }
}
