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
    Task<int> CountActiveTypesAsync();
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
    Task<IEnumerable<EmployeeLeaveBalancesDto>> GetTeamBalancesAsync(string supervisorId, int year);
    Task<IEnumerable<OnLeaveTodayDto>> GetOnLeaveTodayAsync(DateTime today);
    Task<int> CountPendingApprovalsAsync(string reviewerId);

    // Per-employee entitlement override for one year. Ok=false carries why.
    Task<LeaveEntitlementResult> SetEntitlementAsync(
        string employeeId, string leaveTypeId, int year, SetEntitlementDto dto);
    Task<LeaveEntitlementResult> ResetEntitlementAsync(
        string employeeId, string leaveTypeId, int year);

    // Opens the year for ONE employee — the per-person half of the rollover,
    // for someone who joins after it has run.
    Task<int> SeedEntitlementsAsync(string employeeId, int year);

    Task<double> GetApprovedDaysInRangeAsync(string employeeId, DateTime from, DateTime to);
    Task<LeaveOverviewDto> GetOverviewAsync(int year);

    // The yearly summary tables for one employee (JSON), and the same
    // rendered as production's two-page PDF.
    Task<LeaveSummaryReportResult> GetSummaryReportAsync(string employeeId, int year);
    Task<LeaveExportResult> ExportSummaryPdfAsync(string employeeId, int year);

    // One PDF per employee, bundled into a ZIP. Production's rationale: HR
    // forwards individual summaries, and splitting a combined document first
    // is busywork. Empty employeeIds = everyone in the org.
    Task<LeaveExportResult> ExportBulkSummaryZipAsync(int year, IReadOnlyList<string>? employeeIds);
    Task<LeaveAttachmentResult> GetAttachmentAsync(string xeroFileId);
    Task<LeaveApplyResult> ApplyAsync(CreateLeaveApplicationDto dto, string employeeId);
    Task<LeaveApplyResult> EditAsync(string id, CreateLeaveApplicationDto dto, string actorUserId);

    // An admin files leave FOR an employee. Lands APPROVED — the admin already
    // has authority to grant — and records who did it.
    Task<LeaveApplyResult> ApplyOnBehalfAsync(
        string employeeId, CreateLeaveApplicationDto dto, string adminUserId);

    // The decision trail for one request.
    Task<LeaveAuditResult> GetAuditTrailAsync(string applicationId);
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
public record LeaveAuditResult(bool Found, bool Allowed, IEnumerable<LeaveApprovalEntryDto>? Entries);

public record LeaveSummaryReportResult(bool Found, bool Allowed, LeaveSummaryReportDto? Report);

public record LeaveEntitlementResult(bool Ok, LeaveBalanceDto? Balance, string? Error);

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
