using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class LeaveTypeEligibleGender : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EligibleGender",
                table: "LeaveTypes",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            // Existing companies get the same defaults a new one does:
            // maternity for women, paternity for men. Matched by the default
            // codes, so a type a company created itself is left for everyone.
            migrationBuilder.Sql(
                "UPDATE LeaveTypes SET EligibleGender = 'FEMALE' WHERE UPPER(TRIM(Code)) = 'MATERNITY';");
            migrationBuilder.Sql(
                "UPDATE LeaveTypes SET EligibleGender = 'MALE' WHERE UPPER(TRIM(Code)) = 'PATERNITY';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EligibleGender",
                table: "LeaveTypes");
        }
    }
}
