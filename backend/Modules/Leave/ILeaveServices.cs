using AltomateHR.Api.Modules.Leave.Dtos;

namespace AltomateHR.Api.Modules.Leave;

public interface ILeaveTypeService
{
    Task<IEnumerable<LeaveTypeDto>> GetAllAsync();
    Task<LeaveTypeSaveResult> CreateAsync(SaveLeaveTypeDto dto);
    Task<LeaveTypeSaveResult> UpdateAsync(string id, SaveLeaveTypeDto dto);
    Task<LeaveTypeSaveResult> SetArchivedAsync(string id, bool archived);

    // Creates any of the default leave types the current org is missing.
    // Idempotent — matches on code, so re-running adds nothing.
    Task<int> EnsureDefaultsAsync();
}

public interface ILeaveService
{
    Task<IEnumerable<LeaveApplicationDto>> GetMineAsync(string userId);
    Task<IEnumerable<LeaveApplicationDto>> GetTeamAsync(string userId);
    Task<IEnumerable<LeaveBalanceDto>> GetBalancesAsync(string employeeId, int year);
    Task<LeaveBalancesResult> GetBalancesForEmployeeAsync(string employeeId, int year);
    Task<IEnumerable<EmployeeLeaveBalancesDto>> GetOrgBalancesAsync(int year);
    Task<LeaveExportResult> ExportBalancesCsvAsync(string employeeId, int year);
    Task<LeaveExportResult> ExportOrgBalancesCsvAsync(int year);
    Task<LeaveAttachmentResult> GetAttachmentAsync(string xeroFileId);
    Task<LeaveApplyResult> ApplyAsync(CreateLeaveApplicationDto dto, string employeeId);
    Task<LeaveTransitionResult> ApproveAsync(string id, string approverId);
    Task<LeaveTransitionResult> RejectAsync(string id, string approverId, string? reviewNotes);
    Task<LeaveTransitionResult> CancelAsync(string id, string userId);
}

// Ok=false carries a human-readable Error (e.g. duplicate code, bad dates).
public record LeaveTypeSaveResult(bool Ok, LeaveTypeDto? Type, string? Error);
public record LeaveApplyResult(bool Ok, LeaveApplicationDto? Application, string? Error);

// Found=false   → not a member of the current org (404).
// Allowed=false → a member, but the caller may not read their balances (403).
// Checked in that order so an outsider's id is indistinguishable from a
// nonexistent one, while an in-org refusal is honest about being a refusal.
public record LeaveBalancesResult(
    bool Found,
    bool Allowed,
    IEnumerable<LeaveBalanceDto> Balances,
    int Year);

// Same Found/Allowed contract as LeaveBalancesResult, plus the rendered file.
public record LeaveExportResult(
    bool Found,
    bool Allowed,
    byte[] Content,
    string FileName);

// Found=false covers BOTH "no such file" and "not a file you may see" — a 404
// either way, so the existence of another team's attachments isn't leaked.
public record LeaveAttachmentResult(
    bool Found,
    byte[] Content,
    string ContentType,
    string FileName);

// Mirrors the claims transition result: Found=false → 404; Transitioned=false
// with an Error → 400; otherwise 200 with the updated Application.
public record LeaveTransitionResult(
    bool Found,
    bool Transitioned,
    LeaveApplicationDto? Application,
    string? Error = null);
