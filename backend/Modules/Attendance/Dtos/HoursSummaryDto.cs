namespace AltomateHR.Api.Modules.Attendance.Dtos;

// Worked-minutes breakdown for one employee over a date range. Mirrors the
// reference app's HoursBuckets shape, minus PublicHolidayMin (no public
// holiday model in this backend) and the always-zero per-record OtMin
// bucket (OT is entirely submission-driven via the Overtime module, never
// derived from clock-in/out duration — see HoursSummaryService).
public class HoursBucketsDto
{
    public int NormalMin { get; set; }
    public int RestDayMin { get; set; }
    public int TotalMin { get; set; }
    public int OtApprovedMin { get; set; }
    public int OtPendingMin { get; set; }
    public int OtRejectedMin { get; set; }

    // Scheduled days in range × the employee's standard daily minutes (from
    // their assigned Shift, else the org's WorkingHoursStart/End). A pure
    // "scheduled days × daily hours" target — leave/holidays aren't subtracted.
    public int ExpectedMin { get; set; }
}

public class EmployeeHoursSummaryDto
{
    public string EmployeeId { get; set; } = string.Empty;
    public string? Email { get; set; }
    public HoursBucketsDto Buckets { get; set; } = new();
}

public class HoursSummaryDto
{
    public HoursBucketsDto Totals { get; set; } = new();
    public List<EmployeeHoursSummaryDto> Employees { get; set; } = [];
}
