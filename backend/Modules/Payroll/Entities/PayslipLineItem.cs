using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Payroll.Entities;

// One allowance, deduction or reimbursement line on a payslip.
//
// ⚠️ These rows are WIPED AND REBUILT on every generation. Nothing that must
// survive a regeneration may live here alone — that is what PayrollRunAdjustment
// and PayrollRunClaim are for (phase 4). Treat this table as derived output.
public class PayslipLineItem : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;   // tenant

    [MaxLength(40)] public string PayslipId { get; set; } = string.Empty;

    public PayslipLineKind Kind { get; set; }

    [MaxLength(160)] public string Label { get; set; } = string.Empty;

    // Always positive. The kind decides the sign at calculation time.
    [Precision(12, 2)] public decimal Amount { get; set; }

    // The `PayrollAdjustmentCategories` code this line came from. Null for
    // free-form manual deductions and for claim reimbursements, which map to no
    // category. Snapshotted so next month's run can total the year by category
    // and enforce the annual exemption ceilings.
    [MaxLength(60)] public string? Category { get; set; }

    // How much of `Amount` is PCB-taxable, once the category's annual exemption
    // ceiling has been applied. Null means no clamp happened and readers should
    // use `Amount` — which is also what legacy rows written before this column
    // existed require. Without it, an exempted portion would leak back into next
    // month's LHDN Y accumulation.
    [Precision(12, 2)] public decimal? PcbTaxableAmount { get; set; }

    // The claim this reimbursement came from (phase 4). Null otherwise.
    [MaxLength(40)] public string? ClaimId { get; set; }

    // Snapshotted from the category rather than re-read at query time — the
    // catalogue can change, a filed payslip cannot.
    public bool SubjectToEpf { get; set; }
    public bool SubjectToSocso { get; set; }
    public bool SubjectToEis { get; set; }
    public bool SubjectToPcb { get; set; }

    public DateTime CreatedAt { get; set; }
}
