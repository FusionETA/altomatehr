using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Leave.Entities;

// An employee's request for leave against a LeaveType. The approval chain is
// admin decision for now (no supervisor chain, attachments or half-days yet).
public class LeaveApplication : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;   // tenant — auto-stamped + auto-filtered

    [MaxLength(40)]
    public string EmployeeId { get; set; } = string.Empty;

    [MaxLength(40)]
    public string LeaveTypeId { get; set; } = string.Empty;

    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    // Inclusive calendar-day span for now (weekend/holiday exclusion deferred).
    // FULL_DAY, or half a day. A half-day must start and end on the same date
    // and counts as 0.5.
    public LeaveDuration Duration { get; set; } = LeaveDuration.FULL_DAY;

    public double TotalDays { get; set; }

    public string? Reason { get; set; }

    public LeaveStatus Status { get; set; } = LeaveStatus.PENDING;

    // Current position in the approval chain (0-based). Advances as each step's
    // approver signs off; the request stays PENDING until the final step.
    public int CurrentStep { get; set; }

    // Supporting document (MC, etc.) stored in XERO FILES, not locally — the
    // id only. Content is proxied through the API so the OAuth token never
    // reaches the browser. Null = no attachment.
    [MaxLength(80)]
    public string? XeroFileId { get; set; }

    // The file's display name, so a download can be labelled without calling Xero.
    [MaxLength(260)]
    public string? AttachmentName { get; set; }

    // Set when an ADMIN filed on the employee's behalf, so the audit trail
    // doesn't claim the employee did it themselves.
    [MaxLength(40)]
    public string? AppliedByAdminId { get; set; }

    // JSON trail of every step: who decided, what, when, with what notes.
    // ReviewNotes/DecidedAt hold only the LAST decision, so a multi-step chain
    // lost its history — this is what makes the audit log possible.
    public string? Approvals { get; set; }

    public string? ReviewNotes { get; set; }
    public DateTime? DecidedAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
