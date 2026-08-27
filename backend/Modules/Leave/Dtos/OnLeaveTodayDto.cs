namespace AltomateHR.Api.Modules.Leave.Dtos;

// One person out on approved leave today — for the admin "who's out" panel.
// Mirrors production's OnLeaveTodayEntry.
public class OnLeaveTodayDto
{
    public string EmployeeId { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string LeaveTypeId { get; set; } = string.Empty;
    public string LeaveTypeCode { get; set; } = string.Empty;
    public string LeaveTypeName { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public double TotalDays { get; set; }
}
