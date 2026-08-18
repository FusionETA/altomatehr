using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Modules.Overtime.Entities;

namespace AltomateHR.Api.Modules.Overtime.Dtos;

public class OvertimeRequestDto
{
    public string Id { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public string? EmployeeEmail { get; set; }
    public string? ProjectId { get; set; }
    public string WorkDate { get; set; } = string.Empty;
    public string StartAt { get; set; } = string.Empty;
    public string EndAt { get; set; } = string.Empty;
    public int RequestedMinutes { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string BeforePhotoUrl { get; set; } = string.Empty;
    public string? AfterPhotoUrl { get; set; }
    public OvertimeStatus Status { get; set; }
    public int CurrentStep { get; set; }
    public string? ReviewNotes { get; set; }
    public string SubmittedAt { get; set; } = string.Empty;
    public string? DecidedAt { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
}

public class CreateOvertimeRequestDto
{
    [MaxLength(40)]
    public string? ProjectId { get; set; }

    [Required]
    public DateTime? WorkDate { get; set; }

    [Required]
    public DateTime? StartAt { get; set; }

    [Required]
    public DateTime? EndAt { get; set; }

    [Required, MaxLength(1000)]
    public string Reason { get; set; } = string.Empty;

    [Required, MaxLength(1000)]
    public string BeforePhotoUrl { get; set; } = string.Empty;
}

public class AttachOvertimeAfterPhotoDto
{
    [Required, MaxLength(1000)]
    public string AfterPhotoUrl { get; set; } = string.Empty;
}

public class RejectOvertimeDto
{
    [MaxLength(1000)]
    public string? ReviewNotes { get; set; }
}
