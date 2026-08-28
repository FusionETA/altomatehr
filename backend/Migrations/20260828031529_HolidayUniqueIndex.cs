using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class HolidayUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Holidays_Date_ProjectId",
                table: "Holidays");

            migrationBuilder.CreateIndex(
                name: "IX_Holidays_OrganizationId_Date_ProjectId",
                table: "Holidays",
                columns: new[] { "OrganizationId", "Date", "ProjectId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Holidays_OrganizationId_Date_ProjectId",
                table: "Holidays");

            migrationBuilder.CreateIndex(
                name: "IX_Holidays_Date_ProjectId",
                table: "Holidays",
                columns: new[] { "Date", "ProjectId" });
        }
    }
}
