using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Modules.Leave.Entities;

namespace AltomateHR.Api.Modules.Leave.Dtos;

// Response shape for a leave application. Dates are plain calendar dates
// (yyyy-MM-dd); DecidedAt/CreatedAt are ISO-8601 UTC strings.
public class LeaveApplicationDto
{
    public string Id { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public string? EmployeeEmail { get; set; }   // populated for team/approver views
    public string LeaveTypeId { get; set; } = string.Empty;
    public string StartDate { get; set; } = string.Empty;
    public string EndDate { get; set; } = string.Empty;
    public double TotalDays { get; set; }
    public string? Reason { get; set; }
    public LeaveStatus Status { get; set; }
    public string? ReviewNotes { get; set; }
    public string? DecidedAt { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
}

public class CreateLeaveApplicationDto
{
    [Required, MaxLength(40)]
    public string LeaveTypeId { get; set; } = string.Empty;

    [Required]
    public DateTime? StartDate { get; set; }

    [Required]
    public DateTime? EndDate { get; set; }

    [MaxLength(1000)]
    public string? Reason { get; set; }
}

public class RejectLeaveDto
{
    [MaxLength(1000)]
    public string? ReviewNotes { get; set; }
}
