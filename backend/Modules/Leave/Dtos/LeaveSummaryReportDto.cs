namespace AltomateHR.Api.Modules.Leave.Dtos;

// The two tables behind the yearly leave summary, ported from production's
// buildEmployeeSection. Exposed as JSON as well as PDF so the frontend can
// render the same report without a round-trip through a document.
public class LeaveSummaryReportDto
{
    public string OrganizationName { get; set; } = string.Empty;
    public string EmployeeLabel { get; set; } = string.Empty;
    public int Year { get; set; }
    public DateTime ReportDate { get; set; }

    public IEnumerable<LeaveMonthlyRowDto> MonthlyRows { get; set; } = [];
    public IEnumerable<LeaveDetailRowDto> DetailRows { get; set; } = [];
}

// One leave type across the year. Monthly holds 12 entries, Jan-Dec; null
// means no leave that month (rendered as "–", never "0").
public class LeaveMonthlyRowDto
{
    public string LeaveTypeName { get; set; } = string.Empty;
    public double EntitledDays { get; set; }
    public double CarriedDays { get; set; }
    public IReadOnlyList<double?> Monthly { get; set; } = [];
    public double Total { get; set; }

    // Production computes this as Entitled + Carried − Total. Note it uses
    // ENTITLED, not accrued: the report shows the year's full allowance, not
    // what has accrued to date. Can go negative, and prints red when it does.
    public double Balance { get; set; }
}

public class LeaveDetailRowDto
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public string LeaveTypeName { get; set; } = string.Empty;
    public double Days { get; set; }
    public string? Reason { get; set; }
    public string? AttachmentName { get; set; }
}
