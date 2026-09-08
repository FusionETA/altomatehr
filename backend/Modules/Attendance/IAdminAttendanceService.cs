using AltomateHR.Api.Modules.Attendance.Dtos;

namespace AltomateHR.Api.Modules.Attendance;

// The org-wide attendance reports the admin portal reads. Everything here spans
// the whole organisation, so every method is admin-gated at the controller; the
// tenant filter is what keeps it to one org.
//
// `projectId`, `teamId` and `q` narrow every report the same way, through one
// shared employee-scoping step — so the three tabs cannot disagree about who a
// filter means.
public interface IAdminAttendanceService
{
    // How long each supervisor takes to decide, against the org's SLA.
    Task<IReadOnlyList<SupervisorPerformanceDto>> GetSupervisorPerformanceAsync(
        DateTime from, DateTime to, string? projectId, string? teamId, string? q);

    // Who decided what. Pending rows are included so a request nobody has
    // touched sits alongside the slow decisions rather than being invisible.
    Task<IReadOnlyList<ApprovalAuditEntryDto>> GetApprovalAuditAsync(
        DateTime from, DateTime to, string? projectId, string? teamId, string? q);

    // What the clock-in selfies are costing in storage.
    Task<SelfieStorageDto> GetSelfieStorageAsync();
}
