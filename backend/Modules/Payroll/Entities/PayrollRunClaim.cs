using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Payroll.Entities;

// An approved expense claim attached to a payroll run, to be paid back through
// the employee's pay rather than through Xero.
//
// Like `PayrollRunAdjustment`, this row exists because generation is
// destructive: the REIMBURSEMENT line item it produces is rebuilt on every
// Generate press, so the attachment itself has to survive somewhere else. The
// generated `PayslipLineItem` carries `ClaimId` back to here for traceability.
//
// `Label` and `Amount` are SNAPSHOTTED at attach time. Editing the underlying
// claim afterwards must not move a figure on a payroll run that has already
// been generated — still less one that has been filed.
//
// A claim can sit on at most one run, ever: `ClaimId` is globally unique here,
// so double-paying a reimbursement is a constraint violation rather than a
// reconciliation problem discovered later.
public class PayrollRunClaim : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;   // tenant

    [MaxLength(40)]
    public string PayrollRunId { get; set; } = string.Empty;

    [MaxLength(40)]
    public string ClaimId { get; set; } = string.Empty;          // unique across all runs

    [MaxLength(40)]
    public string EmployeeProfileId { get; set; } = string.Empty;

    // Snapshot of the claim's title at attach time.
    [MaxLength(200)]
    public string Label { get; set; } = string.Empty;

    // Snapshot of the claim's amount at attach time.
    [Precision(12, 2)] public decimal Amount { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
