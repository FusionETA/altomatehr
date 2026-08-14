using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Leave.Entities;

// An employee's request for leave against a LeaveType. Approvals are a single
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
    public double TotalDays { get; set; }

    public string? Reason { get; set; }

    public LeaveStatus Status { get; set; } = LeaveStatus.PENDING;

    // Current position in the approval chain (0-based). Advances as each step's
    // approver signs off; the request stays PENDING until the final step.
    public int CurrentStep { get; set; }

    public string? ReviewNotes { get; set; }
    public DateTime? DecidedAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
