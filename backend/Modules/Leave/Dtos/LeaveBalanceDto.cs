namespace AltomateHR.Api.Modules.Leave.Dtos;

// Per-type leave balance for one employee this year:
//   Remaining = Entitlement − Taken (approved). Pending is shown separately so
//   the employee sees days awaiting approval without them reducing the balance.
public class LeaveBalanceDto
{
    public string LeaveTypeId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Paid { get; set; }
    public double EntitlementDays { get; set; }
    public double TakenDays { get; set; }
    public double PendingDays { get; set; }
    public double RemainingDays { get; set; }
}
