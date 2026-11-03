using AltomateHR.Api.Modules.Leave.Entities;

namespace AltomateHR.Api.Modules.Leave.Dtos;

// One leave application's part inside a window (a month), whatever its status
// or type — payroll's view for settling unpaid leave decided after its month
// was filed. `Days` is the clipped part counted on the working-day calendar,
// exactly as GetApprovedUnpaidDaysForOrgAsync counts it.
public sealed record UnpaidLeavePartDto(
    string ApplicationId,
    string UserId,
    string LeaveTypeId,
    bool TypePaid,
    LeaveStatus Status,
    DateTime StartDate,
    DateTime EndDate,
    DateTime? DecidedAt,
    double Days)
{
    public bool IsApprovedUnpaid => Status == LeaveStatus.APPROVED && !TypePaid;
}
