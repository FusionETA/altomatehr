using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class GrantAppraisifyNotificationsScope : Migration
    {
        // Data-only migration: grants the "appraisify" ApiClient row the new
        // notifications:write scope (see ApiScopes.cs / PartnerAuthService.
        // SendNotificationAsync). The row predates this scope's existence
        // (seeded with just "employees:read"), and DbSeeder's own insert is
        // idempotent — it skips existing rows — so updating the seed source
        // alone never reaches this already-seeded row. Scoped tightly to the
        // exact known current value so it's a no-op if anything about the row
        // has already changed since this was written.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE ApiClients SET Scopes = 'employees:read,notifications:write' " +
                "WHERE Name = 'appraisify' AND Scopes = 'employees:read';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE ApiClients SET Scopes = 'employees:read' " +
                "WHERE Name = 'appraisify' AND Scopes = 'employees:read,notifications:write';");
        }
    }
}
