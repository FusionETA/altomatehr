using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Modules.Claims.Entities;

namespace AltomateHR.Api.Modules.Payroll.Dtos;

// Attach an approved claim to a draft run.
public class AttachPayrollRunClaimDto
{
    [Required]
    [MaxLength(40)]
    public string ClaimId { get; set; } = string.Empty;
}

// A claim already attached to a run. Label and Amount are the values
// SNAPSHOTTED at attach time, not the claim's current ones — editing the claim
// afterwards must not move a figure on a run that has been generated.
public class PayrollRunClaimDto
{
    public string Id { get; set; } = string.Empty;
    public string PayrollRunId { get; set; } = string.Empty;
    public string ClaimId { get; set; } = string.Empty;
    public string EmployeeProfileId { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;
    public decimal Amount { get; set; }

    // Joined for display, so the run page renders without a second round trip.
    public string EmployeeName { get; set; } = string.Empty;
    public string? EmployeeNumber { get; set; }
    public string ClaimNumber { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}

// A claim that COULD be attached: approved, personal-paid and routed to
// payroll settlement. Rows already attached are still listed, flagged with
// where they sit, so the picker can grey them out and say why.
public class AttachableClaimDto
{
    public string ClaimId { get; set; } = string.Empty;
    public string ClaimNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public ClaimCategory Category { get; set; }
    public ClaimType ClaimType { get; set; }
    public decimal Amount { get; set; }
    public DateTime SpentAt { get; set; }

    public string UserId { get; set; } = string.Empty;
    public string? EmployeeProfileId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string? EmployeeNumber { get; set; }

    // Null = free to attach. Otherwise the run holding it, with its period
    // spelled out so the admin knows where to go and detach.
    public string? AttachedToRunId { get; set; }
    public string? AttachedToRunPeriod { get; set; }

    // Why this claim cannot be attached right now, if it cannot. Null means it
    // can. The commonest case is a submitter with no employee profile: there is
    // no one on the payroll to pay it to.
    public string? BlockedReason { get; set; }
}

public sealed record PayrollRunClaimAttachResult(
    bool Found, bool Ok, PayrollRunClaimDto? Attachment, string? Error);

public sealed record PayrollRunClaimDetachResult(bool Found, bool Ok, string? Error);
