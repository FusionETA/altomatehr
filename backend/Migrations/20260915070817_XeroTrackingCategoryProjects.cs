using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class XeroTrackingCategoryProjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProjectTrackingCategoryId",
                table: "XeroConnections",
                type: "varchar(80)",
                maxLength: 80,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ProjectTrackingCategoryName",
                table: "XeroConnections",
                type: "varchar(160)",
                maxLength: 160,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "XeroTrackingCategoryId",
                table: "Projects",
                type: "varchar(80)",
                maxLength: 80,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "XeroTrackingOptionId",
                table: "Projects",
                type: "varchar(80)",
                maxLength: 80,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProjectTrackingCategoryId",
                table: "XeroConnections");

            migrationBuilder.DropColumn(
                name: "ProjectTrackingCategoryName",
                table: "XeroConnections");

            migrationBuilder.DropColumn(
                name: "XeroTrackingCategoryId",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "XeroTrackingOptionId",
                table: "Projects");
        }
    }
}
