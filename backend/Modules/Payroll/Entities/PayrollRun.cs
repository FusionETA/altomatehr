using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;
using Microsoft.EntityFrameworkCore;

// XeroSyncStatus is shared with Claims — one vocabulary for "has this
// reached Xero" across the app.
using AltomateHR.Api.Modules.Claims.Entities;

namespace AltomateHR.Api.Modules.Payroll.Entities;

// One month of payroll for one organization.
//
// At most one run per (org, period) — the unique index in AppDbContext enforces
// it — so "the January run" is unambiguous for every downstream filing.
//
// The totals are denormalised sums over the run's payslips, recomputed on each
// generation. They exist so the runs list can render without loading thousands
// of payslips, not as a second source of truth: a payslip is always the answer
// when the two disagree.
public class PayrollRun : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;   // tenant

    public int PeriodYear { get; set; }
    public int PeriodMonth { get; set; }                         // 1–12

    public PayrollRunStatus Status { get; set; } = PayrollRunStatus.DRAFT;
    public PayrollRunSource Source { get; set; } = PayrollRunSource.COMPUTED;

    // ---- Totals across the run's payslips ----

    public int EmployeeCount { get; set; }

    [Precision(14, 2)] public decimal TotalGross { get; set; }
    [Precision(14, 2)] public decimal TotalNet { get; set; }
    [Precision(14, 2)] public decimal TotalEmployeeEpf { get; set; }
    [Precision(14, 2)] public decimal TotalEmployerEpf { get; set; }
    [Precision(14, 2)] public decimal TotalEmployeeSocso { get; set; }
    [Precision(14, 2)] public decimal TotalEmployerSocso { get; set; }
    [Precision(14, 2)] public decimal TotalEmployeeEis { get; set; }
    [Precision(14, 2)] public decimal TotalEmployerEis { get; set; }
    [Precision(14, 2)] public decimal TotalEmployeeSkbbk { get; set; }
    [Precision(14, 2)] public decimal TotalPcb { get; set; }
    [Precision(14, 2)] public decimal TotalCp38 { get; set; }
    [Precision(14, 2)] public decimal TotalZakat { get; set; }

    // HRDF is levied on Malaysian citizens only (PSMB Act 2001 s.2), so the
    // headcount and wage base for it are tracked apart from the run's totals —
    // the levy return asks for exactly these two figures.
    [Precision(14, 2)] public decimal TotalHrdf { get; set; }
    public int EmployeesSubjectToHrdf { get; set; }
    [Precision(14, 2)] public decimal TotalWagesSubjectToHrdf { get; set; }

    // Gross + employer EPF + employer SOCSO + employer EIS + HRDF.
    [Precision(14, 2)] public decimal TotalCostToEmployer { get; set; }

    // ---- Approval trail ----
    //
    // Two steps, two actors, recorded separately. The person who proposes a
    // month's pay and the person who puts it live are the ones an auditor asks
    // about, and collapsing them into one "submitted by" loses the question.
    //
    // Nullable throughout: a run that has never left DRAFT has none of this,
    // and a revert clears it so a re-submission records who did it THIS time.

    public DateTime? SubmittedForApprovalAt { get; set; }

    [MaxLength(40)]
    public string? SubmittedForApprovalById { get; set; }

    // When the run went live. This is the timestamp every later run's YTD
    // depends on, because GetYtdByEmployeeAsync counts SUBMITTED runs only.
    public DateTime? SubmittedAt { get; set; }

    [MaxLength(40)]
    public string? SubmittedById { get; set; }

    // Why an approver sent a pending run back. Kept on the run after it
    // returns to DRAFT so the original submitter can see what to fix; cleared
    // on the next submission.
    public string? ApprovalRejectionReason { get; set; }

    // ---- Generation / staleness ----

    // When payslips were last generated. Null means the run exists but has never
    // been generated.
    public DateTime? GeneratedAt { get; set; }

    // Last content mutation that has NOT been folded into the payslips — set
    // when an adjustment or claim changes (phase 4), cleared on generation. The
    // run is stale while this is non-null, which is the signal the detail page
    // needs to warn that the figures on screen are behind their inputs.
    public DateTime? LastMutatedAt { get; set; }

    // ---- Xero ----

    // The manual journal this run posted, once it has. Unique so the same run
    // can never be recorded against two journals, and the presence of a value
    // is what stops a second post.
    [MaxLength(80)]
    public string? XeroManualJournalId { get; set; }

    // Echoed back from Xero so an admin can find the entry there by name
    // rather than by id.
    [MaxLength(200)]
    public string? XeroJournalNumber { get; set; }

    public XeroSyncStatus XeroSyncStatus { get; set; } = XeroSyncStatus.NOT_SYNCED;

    // Why the last attempt failed. Cleared by a retry that succeeds — a stale
    // error beside a posted journal reads as a problem that is not there.
    public string? XeroSyncError { get; set; }

    public DateTime? XeroSyncedAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
