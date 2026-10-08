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

            // Until now "configured" meant "a row exists". Decide it from
            // evidence instead of guessing from timestamps:
            //   • a row older than the year-end start-month column
            //     (20261008083407_PayrollStartMonth) was written by a General
            //     save or onboarding — onboarding's rows even carry a zero date;
            //   • a newer row counts only if a General save is in its audit
            //     trail ("Configured/Updated payroll settings" — the start-month
            //     endpoint writes "Set payroll as started…" / "Cleared…").
            // A row created by the start month alone, however often the month
            // was changed or cleared since, stays unconfigured.
            migrationBuilder.Sql(@"
                UPDATE PayrollSettings s SET s.ConfiguredAt = s.UpdatedAt
                WHERE s.CreatedAt < '2026-10-08 08:34:07'
                   OR EXISTS (SELECT 1 FROM AuditLogs a
                              WHERE a.OrganizationId = s.OrganizationId
                                AND a.TargetType = 'PayrollSettings'
                                AND a.TargetId = s.Id
                                AND a.Summary IN ('Configured payroll settings', 'Updated payroll settings'));");
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
