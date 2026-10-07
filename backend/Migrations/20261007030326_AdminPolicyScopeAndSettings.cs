using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class AdminPolicyScopeAndSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CanChangeSettings",
                table: "OrganizationMemberships",
                type: "tinyint(1)",
                nullable: false,
                // Existing admins keep the settings access they have today.
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "PolicyScope",
                table: "OrganizationMemberships",
                type: "varchar(4000)",
                maxLength: 4000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CanChangeSettings",
                table: "OrganizationMemberships");

            migrationBuilder.DropColumn(
                name: "PolicyScope",
                table: "OrganizationMemberships");
        }
    }
}
