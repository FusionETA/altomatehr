using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class PayrollSettingsConfiguredAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ConfiguredAt",
                table: "PayrollSettings",
                type: "datetime(6)",
                nullable: true);

            // Until now "configured" meant "a row exists", and every row was
            // written by a General save or onboarding — except one created only
            // by setting the year-end start month (shipped 2026-10-08, PR #61):
            // that leaves a start month on a row nobody ever updated.
            migrationBuilder.Sql(@"
                UPDATE PayrollSettings SET ConfiguredAt = UpdatedAt
                WHERE NOT (PayrollStartYear IS NOT NULL
                           AND CreatedAt = UpdatedAt
                           AND CreatedAt >= '2026-10-08 08:49:00');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConfiguredAt",
                table: "PayrollSettings");
        }
    }
}
