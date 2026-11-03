using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    // Data only. Before payroll fell back to the age-based SOCSO scheme, a blank
    // scheme paid no SOCSO — and in v1 blank was the only way to say "not
    // contributing", so migrated profiles carry it on purpose. Someone with no
    // scheme AND no SOCSO number was never contributing: mark them NOT_COVERED
    // so the fallback leaves them at zero. A blank scheme WITH a SOCSO number
    // keeps the fallback (that was the go-live bug: covered staff paying none).
    /// <inheritdoc />
    public partial class BlankSocsoSchemeWithoutNumberNotCovered : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE `EmployeeProfiles`
                SET `SocsoScheme` = 'NOT_COVERED'
                WHERE `SocsoScheme` IS NULL
                  AND (`SocsoNumber` IS NULL OR TRIM(`SocsoNumber`) = '');");
        }

        /// <inheritdoc />
        // Not reversible: after Up, an admin's own NOT_COVERED can't be told
        // apart from one this set.
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
