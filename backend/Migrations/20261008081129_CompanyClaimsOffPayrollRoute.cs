using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class CompanyClaimsOffPayrollRoute : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Company-money claims stamped with the Payroll route went nowhere:
            // Xero sync refused them and payroll only reimburses own-money
            // claims. They now follow ClaimsService.SettlementFor — Xero, as
            // Spend Money. Only those not already in Xero.
            migrationBuilder.Sql(
                "UPDATE `Claims` SET `Settlement` = 'XERO_BILL' " +
                "WHERE `PaymentType` = 'COMPANY' AND `Settlement` = 'PAYROLL' " +
                "AND (`XeroBillId` IS NULL OR `XeroBillId` = '');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not reversible: which claims were moved isn't recorded, and
            // moving them back would strand them again.
        }
    }
}
