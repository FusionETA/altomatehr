using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class DefaultClaimItemTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ArchivedByXeroConnect",
                table: "ChartOfAccounts",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDefault",
                table: "ChartOfAccounts",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        
            // One-time backfill: a company with no accounts at all and no Xero
            // connection gets the starter claim item types, as a new company
            // now does at creation (OrganizationDefaultsService).
            migrationBuilder.Sql($@"
                INSERT INTO ChartOfAccounts
                    (Id, OrganizationId, Code, Name, Type, IsCustom, IsDefault, ArchivedByXeroConnect,
                     IsSelectable, AllowMileageClaim, IsArchived, CreatedAt)
                SELECT UUID(), o.Id, d.Code, d.Name, d.Type, 1, 1, 0, 1, 0, 0, UTC_TIMESTAMP(6)
                FROM Organizations o
                CROSS JOIN ({string.Join(" UNION ALL ", AltomateHR.Api.Modules.Accounts.DefaultClaimItemTypes.All.Select(i => $"SELECT '{i.Code}' AS Code, '{i.Name.Replace("'", "''")}' AS Name, '{i.Type}' AS Type"))}) d
                WHERE NOT EXISTS (SELECT 1 FROM ChartOfAccounts a WHERE a.OrganizationId = o.Id)
                  AND NOT EXISTS (SELECT 1 FROM XeroConnections x WHERE x.OrganizationId = o.Id);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArchivedByXeroConnect",
                table: "ChartOfAccounts");

            migrationBuilder.DropColumn(
                name: "IsDefault",
                table: "ChartOfAccounts");
        }
    }
}
