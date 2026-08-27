using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Modules.Attendance.Entities;

namespace AltomateHR.Api.Modules.Attendance.Dtos;

public class StartBreakDto
{
    [Range(-90, 90)]
    public double? Lat { get; set; }

    [Range(-180, 180)]
    public double? Lng { get; set; }

    [MaxLength(1000)]
    public string? Remark { get; set; }
}

public class EndBreakDto
{
    [Range(-90, 90)]
    public double? Lat { get; set; }

    [Range(-180, 180)]
    public double? Lng { get; set; }

    [MaxLength(1000)]
    public string? Remark { get; set; }
}

public class RejectBreakDto
{
    public string? ReviewNotes { get; set; }
}

// Response shape for a break. Instants are ISO-8601 UTC strings (trailing "Z").
public class AttendanceBreakDto
{
    public string Id { get; set; } = string.Empty;
    public string AttendanceSessionId { get; set; } = string.Empty;
    public string AttendanceRecordId { get; set; } = string.Empty;
    public string StartedAt { get; set; } = string.Empty;
    public string? EndedAt { get; set; }
    public int? DurationMin { get; set; }
    public double? StartLat { get; set; }
    public double? StartLng { get; set; }
    public double? EndLat { get; set; }
    public double? EndLng { get; set; }
    public string? Remark { get; set; }

    // "Latest" rollup — see AttendanceRecordDto for the same convention.
    public AttendanceApprovalStatus ApprovalStatus { get; set; }
    public int CurrentStep { get; set; }
    public string? ReviewNotes { get; set; }
    public string? SubmittedAt { get; set; }
    public string? DecidedAt { get; set; }
    public List<AttendanceApprovalRequestDto> Approvals { get; set; } = [];
}
