using AltomateHR.Api.Modules.Leave.Dtos;

namespace AltomateHR.Api.Modules.Leave;

public interface ILeaveTypeService
{
    Task<IEnumerable<LeaveTypeDto>> GetAllAsync();
    Task<LeaveTypeSaveResult> CreateAsync(SaveLeaveTypeDto dto);
    Task<LeaveTypeSaveResult> UpdateAsync(string id, SaveLeaveTypeDto dto);
    Task<LeaveTypeDto?> SetArchivedAsync(string id, bool archived);
}

public interface ILeaveService
{
    Task<IEnumerable<LeaveApplicationDto>> GetMineAsync(string userId);
    Task<IEnumerable<LeaveApplicationDto>> GetTeamAsync(string userId, string? role);
    Task<IEnumerable<LeaveBalanceDto>> GetBalancesAsync(string employeeId);
    Task<LeaveApplyResult> ApplyAsync(CreateLeaveApplicationDto dto, string employeeId);
    Task<LeaveTransitionResult> ApproveAsync(string id, string approverId, string? role);
    Task<LeaveTransitionResult> RejectAsync(string id, string approverId, string? role, string? reviewNotes);
    Task<LeaveTransitionResult> CancelAsync(string id, string userId);
}

// Ok=false carries a human-readable Error (e.g. duplicate code, bad dates).
public record LeaveTypeSaveResult(bool Ok, LeaveTypeDto? Type, string? Error);
public record LeaveApplyResult(bool Ok, LeaveApplicationDto? Application, string? Error);

// Mirrors the claims transition result: Found=false → 404; Transitioned=false
// with an Error → 400; otherwise 200 with the updated Application.
public record LeaveTransitionResult(
    bool Found,
    bool Transitioned,
    LeaveApplicationDto? Application,
    string? Error = null);
