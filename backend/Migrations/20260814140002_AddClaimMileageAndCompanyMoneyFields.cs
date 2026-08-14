using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddClaimMileageAndCompanyMoneyFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MileageUnit",
                table: "Organizations",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "KM")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<decimal>(
                name: "Distance",
                table: "Claims",
                type: "decimal(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MileageDestinationAddress",
                table: "Claims",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "MileageOriginAddress",
                table: "Claims",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<decimal>(
                name: "MileageRateUsed",
                table: "Claims",
                type: "decimal(10,4)",
                precision: 10,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MileageUnitUsed",
                table: "Claims",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "PayViaAccountId",
                table: "Claims",
                type: "varchar(40)",
                maxLength: 40,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "SpendingAt",
                table: "Claims",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "SpendingWith",
                table: "Claims",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MileageUnit",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "Distance",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "MileageDestinationAddress",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "MileageOriginAddress",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "MileageRateUsed",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "MileageUnitUsed",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "PayViaAccountId",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "SpendingAt",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "SpendingWith",
                table: "Claims");
        }
    }
}
