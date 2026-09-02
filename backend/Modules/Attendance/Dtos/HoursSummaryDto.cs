namespace AltomateHR.Api.Modules.Attendance.Dtos;

// Worked-minutes breakdown for one employee over a date range. Mirrors the
// reference app's HoursBuckets shape, minus the always-zero per-record OtMin
// bucket (OT is entirely submission-driven via the Overtime module, never
// derived from clock-in/out duration — see HoursSummaryService).
//
// Day type comes from IOtRateService so the hours panel and the pay rate can
// never disagree about whether a given Saturday was a rest day.
public class HoursBucketsDto
{
    // Working-day minutes, CAPPED at the employee's standard daily minutes.
    // Clocking in late and out late to make the time up still lands a full day;
    // anything past the shift length lands in BeyondShiftMin instead.
    public int NormalMin { get; set; }

    // Non-working-day minutes, uncapped — every minute of it is already outside
    // the schedule, so there is no "normal" portion to cap against.
    public int RestDayMin { get; set; }

    // Minutes worked on a date in the holiday calendar. Beats RestDayMin when a
    // holiday falls on a rest day, matching IOtRateService's precedence.
    public int PublicHolidayMin { get; set; }

    // Working-day minutes past the shift length. Recorded, never paid from here:
    // it takes an approved overtime submission to become money. Surfaced so the
    // UI can say "5m beyond shift, not counted" rather than silently dropping it.
    public int BeyondShiftMin { get; set; }

    // Break minutes deducted from the raw clock time.
    public int BreakMin { get; set; }

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
