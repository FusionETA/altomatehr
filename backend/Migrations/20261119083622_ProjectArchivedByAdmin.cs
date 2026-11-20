using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class ProjectArchivedByAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ArchivedByAdmin",
                table: "Projects",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            // What is archived now while Xero still has it open was archived by
            // someone here (or carried over from v1, where it was disabled), so
            // it is the admin's choice and the next sync must keep it. Closed
            // or archived in Xero stays Xero's call; projects hidden on
            // connecting Xero keep their own flag.
            migrationBuilder.Sql("""
                UPDATE Projects
                   SET ArchivedByAdmin = 1
                 WHERE IsArchived = 1
                   AND ArchivedByXeroConnect = 0
                   AND (XeroStatus IS NULL OR UPPER(XeroStatus) NOT IN ('CLOSED', 'ARCHIVED', 'DELETED'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArchivedByAdmin",
                table: "Projects");
        }
    }
}
