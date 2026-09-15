using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class ProjectArchivedByXeroConnect : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ArchivedByXeroConnect",
                table: "Projects",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            // Backfill: orgs that connected Xero BEFORE this column existed
            // never had their hand-created projects archived, so those projects
            // are still being offered in attendance and company structure.
            // Apply the same rule retroactively — but only where Xero has
            // actually supplied projects, so this can't leave a running org
            // with nothing selectable and nobody able to clock in.
            //
            // That check is a JOIN on a derived table rather than an EXISTS:
            // MySQL refuses to read the table an UPDATE targets from a subquery
            // in its own WHERE (error 1093).
            migrationBuilder.Sql(
                """
                UPDATE Projects p
                JOIN XeroConnections c
                  ON c.OrganizationId = p.OrganizationId
                 AND c.DisconnectedAt IS NULL
                JOIN (
                    SELECT DISTINCT OrganizationId
                    FROM Projects
                    WHERE XeroProjectId IS NOT NULL
                ) synced
                  ON synced.OrganizationId = p.OrganizationId
                SET p.IsArchived = 1,
                    p.ArchivedByXeroConnect = 1
                WHERE p.XeroProjectId IS NULL
                  AND p.IsArchived = 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Un-archive before the flag that records why they were archived
            // goes away with the column.
            migrationBuilder.Sql(
                "UPDATE Projects SET IsArchived = 0 WHERE ArchivedByXeroConnect = 1;");

            migrationBuilder.DropColumn(
                name: "ArchivedByXeroConnect",
                table: "Projects");
        }
    }
}
