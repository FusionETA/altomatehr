using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddOrgClaimSettlementRoute : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClaimSettlementRoute",
                table: "Organizations",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                // "XERO_BILL", not the scaffolded "": the column stores the enum
                // as a string and "" parses to nothing, so every existing org
                // would fail to materialise. It is also the correct history —
                // a Xero bill was the only payout route before this setting.
                defaultValue: "XERO_BILL")
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClaimSettlementRoute",
                table: "Organizations");
        }
    }
}
