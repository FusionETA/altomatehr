using AltomateHR.Api.Common.Tabular;
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

    // Same defaults, for a brand-new org at creation time — before the caller's
    // JWT (and so ICurrentUser.OrganizationId) has been reissued for it, so the
    // ambient-tenant version above can't be used. OrganizationId is passed and
    // set explicitly for that reason.
    Task<int> EnsureDefaultsForOrganizationAsync(string organizationId);
    Task<int> CountActiveTypesAsync();
}

public interface ILeaveService
{
    Task<IEnumerable<LeaveApplicationDto>> GetMineAsync(string userId);
    Task<IEnumerable<LeaveApplicationDto>> GetTeamAsync(string userId);

    // Every application in the org, newest first. Admin history.
    Task<IEnumerable<LeaveApplicationDto>> GetAllForOrgAsync();
    Task<IEnumerable<LeaveBalanceDto>> GetBalancesAsync(string employeeId, int year);
    Task<LeaveBalancesResult> GetBalancesForEmployeeAsync(string employeeId, int year);
    Task<IEnumerable<EmployeeLeaveBalancesDto>> GetOrgBalancesAsync(int year);

    // Balances as CSV or XLSX — one employee, or the whole org.
    Task<LeaveExportResult> ExportBalancesAsync(string employeeId, int year, TabularFormat format);
    Task<LeaveExportResult> ExportOrgBalancesAsync(int year, TabularFormat format);

    // The blank leave-history import template, in either format.
    TabularExportResult BuildImportTemplate(TabularFormat format);

    // Bulk-import historical leave APPLICATIONS (a migration off another
    // system). Append-only and idempotent: a request already on file for the
    // same employee, type and exact dates is skipped, so a re-run can never
    // double-count someone's leave.
    Task<TabularImportResult> ImportHistoryAsync(
        byte[] content, TabularFormat format, string adminUserId);

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

    // Re-derives PRO_RATED accrued days from the employee's join date. Called
    // when that date changes — nothing else recalculates it, because the
    // monthly cron only ever adds. Returns how many rows moved.
    Task<int> RecomputeProRatedAccrualAsync(string employeeId, int year);

    Task<double> GetApprovedDaysInRangeAsync(string employeeId, DateTime from, DateTime to);

    // Approved UNPAID leave days per employee that fall inside [from, to],
    // org-wide in one pass. Keyed by user id; employees with none are absent.
    //
    // Deliberately NOT GetApprovedDaysInRangeAsync's shape. That one sums the
    // whole application whenever it merely OVERLAPS the range, which is fine
    // for a "was this person away" question and wrong for pay: a 28 Jan – 6 Feb
    // absence would be docked in full from January AND again from February.
    // Here the range is CLIPPED first and the days recounted against the same
    // working-day calendar, so each day is paid for — or not — exactly once.
    Task<IReadOnlyDictionary<string, double>> GetApprovedUnpaidDaysForOrgAsync(
        DateTime from, DateTime to);
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

    // Sign off many applications at once, as the current-step approver of each.
    // Independent per-id success/failure, so the result is a report rather than
    // a single verdict. No bulk REJECT counterpart on purpose — see
    // LeaveService.BulkApproveAsync.
    Task<LeaveBulkResult> BulkApproveAsync(IReadOnlyList<string> ids, string approverId);
    Task<LeaveTransitionResult> RejectAsync(string id, string approverId, string? reviewNotes);
    Task<LeaveTransitionResult> CancelAsync(string id, string userId);

    // Resolves PENDING items that no longer have any approver to route to.
    // `apply: false` only counts them. See the implementation for why.
    Task<int> ReconcileUnreachableApprovalsAsync(bool apply);

    // Every reviewer's current pending-leave count, org-wide — consumed by
    // Modules/Approvals/ApprovalDigestService for the daily cross-module digest.
    Task<IReadOnlyList<OrgApprovalDigestEntryDto>> GetOrgApprovalDigestAsync();
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

// Mirrors the claims and attendance bulk contract: per-id success/failure, so a
// run where eighteen of twenty landed reports as exactly that instead of as a
// failed request.
public record LeaveBulkResultItem(string Id, bool Ok, string? Error = null);

public record LeaveBulkResult(int Succeeded, int Failed, IReadOnlyList<LeaveBulkResultItem> Items);
