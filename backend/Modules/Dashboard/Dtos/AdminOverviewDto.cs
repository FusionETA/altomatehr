namespace AltomateHR.Api.Modules.Dashboard.Dtos;

// The admin executive overview — analytics an admin can't get from the per-user
// endpoints. Ports the monolith's AdminExecutiveOverview. Cards are filled in one by
// one; unbuilt cards return empty collections and the UI shows their empty states.
public class AdminOverviewDto
{
    public List<string> EnabledModules { get; set; } = new();
    public List<ProjectClaimSpendDto> ProjectSpend { get; set; } = new();
    public List<AttendanceHealthDto> AttendanceHealth { get; set; } = new();
    public List<SlowOtApproverDto> SlowOtApprovers { get; set; } = new();
    public List<StalePendingClaimDto> StalePendingClaims { get; set; } = new();
    public UpcomingClaimRunDto? UpcomingClaimRun { get; set; }
    public OverturnedSupervisorsDto OverturnedSupervisors { get; set; } = new();
}

// Card 1 — claim spend grouped by project, current month.
public class ProjectClaimSpendDto
{
    public string Project { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public int ClaimCount { get; set; }
}

public class AttendanceHealthDto
{
    public string Project { get; set; } = string.Empty;
    public int Total { get; set; }
    public int OnTime { get; set; }
    public int Late { get; set; }
    public int Missing { get; set; }
    public int OnLeave { get; set; }
}

public class SlowOtApproverDto
{
    public string ReviewerId { get; set; } = string.Empty;
    public string ReviewerName { get; set; } = string.Empty;
    public int ReviewedCount { get; set; }
    public int PendingCount { get; set; }
    public double AverageHours { get; set; }
}

public class StalePendingClaimDto
{
    public string Id { get; set; } = string.Empty;
    public string ClaimNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int DaysPending { get; set; }
}

public class UpcomingClaimRunDto
{
    public DateTime CutoffDate { get; set; }
    public int CutoffDay { get; set; }
    public int DaysUntilCutoff { get; set; }
    public int ClaimsInRun { get; set; }
    public int PendingInRun { get; set; }
    public decimal TotalAmountInRun { get; set; }
}

public class OverturnedSupervisorsDto
{
    public int Total { get; set; }
    public List<OverturnedSupervisorDto> Samples { get; set; } = new();
}

public class OverturnedSupervisorDto
{
    public string SupervisorId { get; set; } = string.Empty;
    public string SupervisorName { get; set; } = string.Empty;
    public int OverturnedCount { get; set; }
    public int AffectedEmployees { get; set; }
}
