using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Modules.Employees.Entities;

namespace AltomateHR.Api.Modules.Employees.Dtos;

// What the employee screen needs to draw the Transfer action: where this admin
// can move the person, and any transfer already queued for them.
public class EmployeeTransferOptionsDto
{
    // Every other company this admin runs that has at least one live policy.
    // Empty → the screen hides the button.
    public List<TransferTargetDto> Targets { get; set; } = [];

    // The queued transfer (PENDING, or FAILED and retrying), if any.
    public EmployeeTransferDto? Pending { get; set; }
}

public class TransferTargetDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    // Live policies, the default first.
    public List<TransferTargetPolicyDto> Policies { get; set; } = [];
}

public class TransferTargetPolicyDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}

public class EmployeeTransferDto
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string TargetOrganizationId { get; set; } = string.Empty;
    public string TargetOrganizationName { get; set; } = string.Empty;
    public string TargetPolicyId { get; set; } = string.Empty;
    public DateTime EffectiveDate { get; set; }
    public bool CopyPayrollInfo { get; set; }
    public string? Notes { get; set; }
    public EmployeeTransferStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ExecutedAt { get; set; }
}

// What an admin sends to schedule (or, for today, run) a transfer.
public class CreateEmployeeTransferDto
{
    [Required, MaxLength(40)]
    public string TargetOrganizationId { get; set; } = string.Empty;

    [Required, MaxLength(40)]
    public string TargetPolicyId { get; set; } = string.Empty;

    // The first day at the target. Today runs it now; a later day queues it.
    [Required]
    public DateTime EffectiveDate { get; set; }

    public bool CopyPayrollInfo { get; set; } = true;

    [MaxLength(500)]
    public string? Notes { get; set; }
}

// One tenure at this company, for the Employment history list.
public class EmploymentPeriodDto
{
    public DateTime? JoinDate { get; set; }
    public DateTime? LeaveDate { get; set; }
    public string? StartReason { get; set; }
    public string? EndReason { get; set; }
    public bool IsCurrent { get; set; }
}
