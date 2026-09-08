using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddClaimSettlementAndRunCutoff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // defaultValue 25, NOT the scaffolded 0: this value backfills every
            // existing org, and 0 is not a day of the month. ClaimRunWindow clamps
            // it to 1, so a 0 here would silently tell every existing tenant their
            // claims run closes on the 1st. 25 matches the entity default.
            migrationBuilder.AddColumn<int>(
                name: "ClaimRunCutoffDay",
                table: "Organizations",
                type: "int",
                nullable: false,
                defaultValue: 25);

            // defaultValue "XERO_BILL", NOT the scaffolded "": the column stores
            // the enum as a string, and "" parses to nothing — every existing claim
            // would fail to materialise. XERO_BILL is also the correct history:
            // before this column, a bill in Xero was the only way a claim got paid.
            migrationBuilder.AddColumn<string>(
                name: "Settlement",
                table: "Claims",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "XERO_BILL")
                .Annotation("MySql:CharSet", "utf8mb4");

            // ClaimStatus no longer has REVIEWED. Any row still carrying it would
            // throw on read, so it is converted here rather than left to fail at
            // runtime. Raw SQL on purpose: this has to reach EVERY tenant's rows,
            // and the global query filter would scope an EF update to one org.
            //
            // APPROVED is the right landing place — REVIEWED meant "settled", and
            // APPROVED is this app's terminal settled state.
            migrationBuilder.Sql(
                "UPDATE `Claims` SET `Status` = 'APPROVED' WHERE `Status` = 'REVIEWED';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Note: the REVIEWED → APPROVED conversion is NOT reversed. Which rows
            // were REVIEWED is not recorded anywhere once converted, so inventing
            // a split here would be worse than leaving them APPROVED.
            migrationBuilder.DropColumn(
                name: "ClaimRunCutoffDay",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "Settlement",
                table: "Claims");
        }
    }
}
