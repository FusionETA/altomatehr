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
}
