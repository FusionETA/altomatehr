namespace AltomateHR.Api.Modules.Leave.Dtos;

// Org-level leave summary for the admin dashboard. Mirrors production's
// LeaveOverviewReport, minus the fields V2 has no column for (employee name,
// half-day duration).
public class LeaveOverviewDto
{
    public int Year { get; set; }
    public LeaveStatusTotalsDto Totals { get; set; } = new();
    public IEnumerable<LeaveDaysByTypeDto> DaysUsedByType { get; set; } = [];
    public IEnumerable<OnLeaveTodayDto> OnLeaveToday { get; set; } = [];
    public IEnumerable<LeaveApplicationDto> RecentApplications { get; set; } = [];
}

public class LeaveStatusTotalsDto
{
    public int Pending { get; set; }
    public int Approved { get; set; }
    public int Rejected { get; set; }
    public int Cancelled { get; set; }
}

public class LeaveDaysByTypeDto
{
    public string LeaveTypeId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Paid { get; set; }
    public double DaysUsed { get; set; }
}

// What an admin sends to override ONE employee's entitlement for a year.
public class SetEntitlementDto
{
    public double EntitledDays { get; set; }

    // null clears the per-employee override, so the method falls back through
    // the policy to the leave type.
    public Entities.LeaveAccrualMethod? AccrualMethod { get; set; }
}
