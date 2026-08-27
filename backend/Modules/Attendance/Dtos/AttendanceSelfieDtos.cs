using System.ComponentModel.DataAnnotations;

namespace AltomateHR.Api.Modules.Attendance.Dtos;

// Read-only summary of how many clock-in/out selfies are currently stored and
// what date range they span. Drives the admin "Selfie storage" card.
public class AttendanceSelfieStorageStatsDto
{
    public int Total { get; set; }
    public string? Oldest { get; set; }   // yyyy-MM-dd, the earliest record date holding a photo
    public string? Newest { get; set; }   // yyyy-MM-dd, the latest record date holding a photo
}

public class DeleteSelfiesInRangeDto
{
    [Required]
    public DateTime From { get; set; }

    [Required]
    public DateTime To { get; set; }
}

public class AttendanceDeleteSelfiesResultDto
{
    public int Scanned { get; set; }
    public int Deleted { get; set; }
    public int Failed { get; set; }
}
