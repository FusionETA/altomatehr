using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Payroll.Entities;

// The org's operational payroll rules — one row per organization.
//
// Deliberately separate from PayrollCompanyInfo: this is HOW payroll runs, that
// is WHO the employer is for filing purposes. They change on completely
// different cadences.
public class PayrollSettings : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;   // tenant — one row per org

    // ---- Statutory basis ----

    // The s.60I ordinary-rate divisor. Does NOT govern incomplete-month
    // proration — s.18A overrides that with calendar days. See PayPeriod.
    public WorkingDaysRule WorkingDaysRule { get; set; } = WorkingDaysRule.TWENTY_SIX;

    // Org-level defaults for new employees. The employee's own profile wins once
    // set, and the statutory branch wins over both — an employee on Part E pays
    // 0% whatever is configured here.
    [Precision(5, 2)] public decimal DefaultEpfEmployeeRate { get; set; } = 11.00m;
    [Precision(5, 2)] public decimal DefaultEpfEmployerRate { get; set; } = 13.00m;

    // HRD Corp levy. Only Malaysian citizens count toward it.
    public bool HrdfEnabled { get; set; }
    [Precision(5, 2)] public decimal? HrdfRate { get; set; }

    // Auto-apply the RM 350 PERKESO relief to PCB without waiting for a Form
    // TP1. On by default — the employer already knows the exact figure, so
    // there's no declaration to wait for. See PcbReliefs.PerkesoCap.
    public bool AutoApplySocsoEisRelief { get; set; } = true;

    // ---- Xero ----

    // Both are gated in the UI behind an active Xero connection.
    public bool SyncClaimsToXeroOnSubmit { get; set; }
    public bool SyncPayrollToXeroOnSubmit { get; set; }

    // Account and tracking-category mapping for the payroll journal. Null means
    // the admin hasn't configured Xero sync, and the sync paths skip with a
    // prompt rather than guessing an account.
    public string? XeroMappingJson { get; set; }

    // ---- Disbursement bank ----

    // Which bank's bulk-payroll format the run downloads produce. Matches an
    // entry in the Malaysian bank list.
    [MaxLength(120)] public string? PayrollBankName { get; set; }
    [MaxLength(160)] public string? PayorAccountHolderName { get; set; }
    [MaxLength(60)] public string? PayorOrganisationCode { get; set; }

    // Public Bank ECP specifics: the 10-digit debiting account that pays the
    // run, and the payor's SWIFT/BIC.
    [MaxLength(20)] public string? EcpPayorAccountNo { get; set; }
    [MaxLength(20)] public string? EcpPayorBic { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
