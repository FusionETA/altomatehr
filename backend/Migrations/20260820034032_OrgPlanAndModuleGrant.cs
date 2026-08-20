using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class OrgPlanAndModuleGrant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Addons",
                table: "Organizations",
                type: "varchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Plan",
                table: "Organizations",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "DIY")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Tier",
                table: "Organizations",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Modules",
                table: "OrganizationMemberships",
                type: "varchar(300)",
                maxLength: 300,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Addons",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "Plan",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "Tier",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "Modules",
                table: "OrganizationMemberships");
        }
    }
}
