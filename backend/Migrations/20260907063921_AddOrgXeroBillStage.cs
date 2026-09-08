using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddOrgXeroBillStage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "XeroBillStage",
                table: "Organizations",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                // "AwaitingPayment", not the scaffolded "": the column holds the
                // enum as a string, "" parses to nothing, and it is the correct
                // history — every bill pushed before this setting existed went
                // out as a live payable.
                defaultValue: "AwaitingPayment")
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "XeroBillStage",
                table: "Organizations");
        }
    }
}
