namespace AltomateHR.Api.Modules.Leave.Dtos;

// One row of the admin org-wide balances grid: who they are, plus their
// per-type balances for the year. Mirrors the production admin Leave →
// Balances screen, which reads the same shape server-side.
//
// Production also carries Name and JobTitle; V2's User is identity-only
// (no name column yet), so those are omitted rather than faked.
public class EmployeeLeaveBalancesDto
{
    public string UserId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public IEnumerable<LeaveBalanceDto> Balances { get; set; } = Array.Empty<LeaveBalanceDto>();

    // Only set on the supervisor "team balances" screen — which team this row is
    // being shown under, so the frontend can offer a switcher the way Attendance's
    // team-presence view does. Null on the admin org-wide grid, and null here too
    // for a direct report who isn't on any team the caller supervises.
    public string? TeamId { get; set; }
    public string? TeamName { get; set; }
    public string? ProjectId { get; set; }
    public string? ProjectName { get; set; }
}
