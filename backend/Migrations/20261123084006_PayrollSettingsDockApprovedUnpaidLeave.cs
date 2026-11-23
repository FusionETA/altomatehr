using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class PayrollSettingsDockApprovedUnpaidLeave : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "UnpaidLeaveFromLeaveModule",
                table: "Payslips",
                type: "tinyint(1)",
                nullable: false,
                // Every payslip so far took its unpaid leave from the Leave
                // module, so carry-forward keeps revisiting them as before.
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "DockApprovedUnpaidLeave",
                table: "PayrollSettings",
                type: "tinyint(1)",
                nullable: false,
                // On for every company that exists today: docking approved
                // unpaid leave is what they all do now. Ayu Borneo's
                // companies are switched off in Payroll Settings.
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UnpaidLeaveFromLeaveModule",
                table: "Payslips");

            migrationBuilder.DropColumn(
                name: "DockApprovedUnpaidLeave",
                table: "PayrollSettings");
        }
    }
}
