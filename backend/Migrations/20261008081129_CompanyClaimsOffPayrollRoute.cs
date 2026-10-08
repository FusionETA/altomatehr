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
            // Spend Money, where the org has a live Xero connection; nowhere
            // (NONE) where it doesn't. Only those not already in Xero.
            const string stuck =
                "`c`.`PaymentType` = 'COMPANY' AND `c`.`Settlement` = 'PAYROLL' " +
                "AND (`c`.`XeroBillId` IS NULL OR `c`.`XeroBillId` = '')";
            const string hasXero =
                "EXISTS (SELECT 1 FROM `XeroConnections` `x` " +
                "WHERE `x`.`OrganizationId` = `c`.`OrganizationId` AND `x`.`DisconnectedAt` IS NULL)";

            migrationBuilder.Sql(
                $"UPDATE `Claims` `c` SET `c`.`Settlement` = 'XERO_BILL' WHERE {stuck} AND {hasXero};");
            migrationBuilder.Sql(
                $"UPDATE `Claims` `c` SET `c`.`Settlement` = 'NONE' WHERE {stuck} AND NOT {hasXero};");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not reversible: which claims were moved isn't recorded, and
            // moving them back would strand them again.
        }
    }
}
