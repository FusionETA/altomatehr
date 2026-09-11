using AltomateHR.Api.Common.Tabular;
using AltomateHR.Api.Modules.Attendance;
using AltomateHR.Api.Modules.Attendance.Dtos;
using AltomateHR.Api.Modules.Leave;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Claims;
using AltomateHR.Api.Modules.Claims.Entities;
using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Organizations.Dtos;
using AltomateHR.Api.Modules.Leave.Dtos;

namespace AltomateHR.Api.Tests.Payroll;

// Seedable stand-ins for the two modules payroll reads at generation time.
//
// Payroll uses exactly one method of each. Everything else throws rather than
// returning a benign default, so a new dependency on Attendance or Leave shows
// up as a failing test naming the method, not as a silent zero in someone's pay.

// Attendance. Seed with `Set(userId, normalMin, expectedMin)`; an unseeded
// employee has no attendance at all, which is what an org that does not track
// it looks like.
internal sealed class StubPayrollHours : IHoursSummaryService
{
    private readonly Dictionary<string, HoursBucketsDto> _byUser = new(StringComparer.Ordinal);

    public void Set(string userId, int normalMin, int expectedMin, int beyondShiftMin = 0) =>
        _byUser[userId] = new HoursBucketsDto
        {
            NormalMin = normalMin,
            ExpectedMin = expectedMin,
            BeyondShiftMin = beyondShiftMin,
            TotalMin = normalMin + beyondShiftMin,
        };

    public Task<IReadOnlyDictionary<string, HoursBucketsDto>> GetHoursForEmployeesAsync(
        IEnumerable<string> employeeIds, DateTime from, DateTime to) =>
        Task.FromResult<IReadOnlyDictionary<string, HoursBucketsDto>>(
            employeeIds
                .Where(_byUser.ContainsKey)
                .Distinct(StringComparer.Ordinal)
                .ToDictionary(id => id, id => _byUser[id], StringComparer.Ordinal));

    public Task<HoursBucketsDto> GetMyHoursSummaryAsync(string employeeId, DateTime from, DateTime to) =>
        throw new NotSupportedException();
    public Task<HoursSummaryDto> GetOrgHoursSummaryAsync(DateTime from, DateTime to, string? teamId) =>
        throw new NotSupportedException();
    public Task<HoursBucketsDto?> GetEmployeeHoursSummaryAsync(
        string employeeId, DateTime from, DateTime to, string requestingUserId, string? requestingRole) =>
        throw new NotSupportedException();
}

// Leave. Seed with `SetUnpaidDays(userId, days)`.
internal sealed class StubPayrollLeave : ILeaveService
{
    private readonly Dictionary<string, double> _unpaidByUser = new(StringComparer.Ordinal);

    public void SetUnpaidDays(string userId, double days) => _unpaidByUser[userId] = days;

    public Task<IReadOnlyDictionary<string, double>> GetApprovedUnpaidDaysForOrgAsync(
        DateTime from, DateTime to) =>
        Task.FromResult<IReadOnlyDictionary<string, double>>(_unpaidByUser);

    public Task<IEnumerable<LeaveApplicationDto>> GetMineAsync(string userId) =>
        throw new NotSupportedException();
    public Task<IEnumerable<LeaveApplicationDto>> GetTeamAsync(string userId) =>
        throw new NotSupportedException();
    public Task<IEnumerable<LeaveApplicationDto>> GetAllForOrgAsync() =>
        throw new NotSupportedException();
    public Task<IEnumerable<LeaveBalanceDto>> GetBalancesAsync(string employeeId, int year) =>
        throw new NotSupportedException();
    public Task<LeaveBalancesResult> GetBalancesForEmployeeAsync(string employeeId, int year) =>
        throw new NotSupportedException();
    public Task<IEnumerable<EmployeeLeaveBalancesDto>> GetOrgBalancesAsync(int year) =>
        throw new NotSupportedException();
    public Task<LeaveExportResult> ExportBalancesAsync(string employeeId, int year, TabularFormat format) =>
        throw new NotSupportedException();
    public Task<LeaveExportResult> ExportOrgBalancesAsync(int year, TabularFormat format) =>
        throw new NotSupportedException();
    public TabularExportResult BuildImportTemplate(TabularFormat format) =>
        throw new NotSupportedException();
    public Task<TabularImportResult> ImportHistoryAsync(
        byte[] content, TabularFormat format, string adminUserId) => throw new NotSupportedException();
    public Task<IEnumerable<EmployeeLeaveBalancesDto>> GetTeamBalancesAsync(
        string supervisorId, int year) => throw new NotSupportedException();
    public Task<IEnumerable<OnLeaveTodayDto>> GetOnLeaveTodayAsync(DateTime today) =>
        throw new NotSupportedException();
    public Task<int> CountPendingApprovalsAsync(string reviewerId) => throw new NotSupportedException();
    public Task<LeaveEntitlementResult> SetEntitlementAsync(
        string employeeId, string leaveTypeId, int year, SetEntitlementDto dto) =>
        throw new NotSupportedException();
    public Task<LeaveEntitlementResult> ResetEntitlementAsync(
        string employeeId, string leaveTypeId, int year) => throw new NotSupportedException();
    public Task<int> SeedEntitlementsAsync(string employeeId, int year) => throw new NotSupportedException();
    public Task<int> RecomputeProRatedAccrualAsync(string employeeId, int year) =>
        throw new NotSupportedException();
    public Task<double> GetApprovedDaysInRangeAsync(string employeeId, DateTime from, DateTime to) =>
        throw new NotSupportedException();
    public Task<LeaveOverviewDto> GetOverviewAsync(int year) => throw new NotSupportedException();
    public Task<LeaveSummaryReportResult> GetSummaryReportAsync(string employeeId, int year) =>
        throw new NotSupportedException();
    public Task<LeaveExportResult> ExportSummaryPdfAsync(string employeeId, int year) =>
        throw new NotSupportedException();
    public Task<LeaveExportResult> ExportBulkSummaryZipAsync(
        int year, IReadOnlyList<string>? employeeIds) => throw new NotSupportedException();
    public Task<LeaveAttachmentResult> GetAttachmentAsync(string xeroFileId) =>
        throw new NotSupportedException();
    public Task<LeaveApplyResult> ApplyAsync(CreateLeaveApplicationDto dto, string employeeId) =>
        throw new NotSupportedException();
    public Task<LeaveApplyResult> EditAsync(string id, CreateLeaveApplicationDto dto, string actorUserId) =>
        throw new NotSupportedException();
    public Task<LeaveApplyResult> ApplyOnBehalfAsync(
        string employeeId, CreateLeaveApplicationDto dto, string adminUserId) =>
        throw new NotSupportedException();
    public Task<LeaveAuditResult> GetAuditTrailAsync(string applicationId) =>
        throw new NotSupportedException();
    public Task<LeaveTransitionResult> ApproveAsync(string id, string approverId) =>
        throw new NotSupportedException();
    public Task<LeaveBulkResult> BulkApproveAsync(IReadOnlyList<string> ids, string approverId) =>
        throw new NotSupportedException();
    public Task<LeaveTransitionResult> RejectAsync(string id, string approverId, string? reviewNotes) =>
        throw new NotSupportedException();
    public Task<LeaveTransitionResult> CancelAsync(string id, string userId) =>
        throw new NotSupportedException();
    public Task<int> ReconcileUnreachableApprovalsAsync(bool apply) => throw new NotSupportedException();
    public Task<IReadOnlyList<AltomateHR.Api.Modules.Leave.Dtos.OrgApprovalDigestEntryDto>>
        GetOrgApprovalDigestAsync() => throw new NotSupportedException();
}

// Organizations. Payroll documents need one thing from it — the org's name,
// as the fallback when Company Info carries no employer name.
internal sealed class StubPayrollOrganizations : IOrganizationService
{
    public string Name { get; set; } = "Globe Engineering Sdn Bhd";

    public Task<OrganizationDto?> GetByIdAsync(string organizationId) =>
        Task.FromResult<OrganizationDto?>(new OrganizationDto
        {
            Id = organizationId,
            Name = Name,
        });

    public Task<OrganizationDto> CreateAsync(CreateOrganizationDto dto, string ownerUserId) =>
        throw new NotSupportedException();
    public Task<OrganizationDto?> UpdateAsync(string organizationId, UpdateOrganizationDto dto) =>
        throw new NotSupportedException();
    public Task<OrganizationDto?> SetClaimSettingsAsync(
        string organizationId, int cutoffDay,
        AltomateHR.Api.Modules.Claims.Entities.ClaimSettlement settlementRoute,
        AltomateHR.Api.Modules.Xero.Dtos.XeroBillStatus xeroBillStage) =>
        throw new NotSupportedException();
    public Task<OrganizationDto?> UpdatePlanAsync(string organizationId, UpdateOrgPlanDto dto) =>
        throw new NotSupportedException();
}

// Payroll runs call into the Xero sync on approval. These scenarios are about
// the payroll decision, not the accounting integration, so this records the
// call and does nothing — which is also what the real service does when the
// org has not opted in.
internal sealed class StubPayrollXeroSync : IPayrollXeroSyncService
{
    public Task<IReadOnlyList<PayrollXeroTrackingCategory>> GetTrackingCategoriesAsync() =>
        Task.FromResult<IReadOnlyList<PayrollXeroTrackingCategory>>([]);

    public List<string> ApprovedRunIds { get; } = [];

    public Task<PayrollXeroPreview?> PreviewAsync(string runId) =>
        Task.FromResult<PayrollXeroPreview?>(null);

    public Task<PayrollXeroSyncResult> SyncAsync(string runId) =>
        Task.FromResult(PayrollXeroSyncResult.NotFound());

    public Task SyncOnApprovalAsync(string runId)
    {
        ApprovedRunIds.Add(runId);
        return Task.CompletedTask;
    }
}

// Only the two reads payroll actually uses. Everything else throws, so a
// new dependency on the Claims module shows up as a failing test rather
// than as a silent null.
internal sealed class FakeClaimsService : IClaimsService
{
    private readonly List<Claim> _claims = [];

    public void Add(Claim claim) => _claims.Add(claim);

    public Task<IReadOnlyList<Claim>> GetPayrollReimbursableAsync() =>
        Task.FromResult<IReadOnlyList<Claim>>(_claims
            .Where(c => c.Settlement == ClaimSettlement.PAYROLL
                     && c.Status == ClaimStatus.APPROVED
                     && c.PaymentType == PaymentType.PERSONAL)
            .ToList());

    public Task<Claim?> GetByIdAsync(string id) =>
        Task.FromResult(_claims.FirstOrDefault(c => c.Id == id));

    public Task<IReadOnlyList<Claim>> GetAllForOrgAsync(string? approverId = null) =>
        Task.FromResult<IReadOnlyList<Claim>>(_claims);

    public Task<IEnumerable<Claim>> GetMineAsync(string userId) => throw new NotSupportedException();
    public Task<IEnumerable<Claim>> GetTeamAsync(string userId) => throw new NotSupportedException();
    public Task<Claim?> GetVisibleByIdAsync(string id, string userId, bool isAdmin) =>
        throw new NotSupportedException();
    public Task<Claim> CreateAsync(AltomateHR.Api.Modules.Claims.Dtos.CreateClaimDto dto, string employeeId) =>
        throw new NotSupportedException();
    public Task<Claim?> UpdateAsync(
        string id, AltomateHR.Api.Modules.Claims.Dtos.CreateClaimDto dto, string userId, bool isAdmin) =>
        throw new NotSupportedException();
    public Task<bool> DeleteAsync(string id) => throw new NotSupportedException();
    public Task<ClaimStatusTransitionResult> ApproveAsync(string id, string approverId) =>
        throw new NotSupportedException();
    public Task<ClaimsBulkResult> BulkApproveAsync(IReadOnlyList<string> ids, string approverId) =>
        throw new NotSupportedException();
    public Task<ClaimXeroSyncResult> SyncToXeroAsync(
        string id, AltomateHR.Api.Modules.Xero.Dtos.XeroBillStatus? status = null) => throw new NotSupportedException();
    public Task<ClaimsBulkResult> BulkSyncToXeroAsync(
        IReadOnlyList<string> ids, AltomateHR.Api.Modules.Xero.Dtos.XeroBillStatus? status = null) =>
        throw new NotSupportedException();
    public Task<ClaimStatusTransitionResult> RejectAsync(
        string id, string approverId, string? reviewNotes) => throw new NotSupportedException();
    public Task<ClaimReceiptUploadResult> StoreReceiptAsync(ClaimReceiptUpload upload) =>
        throw new NotSupportedException();
    public Task<ClaimReceiptFileResult?> GetReceiptForUserAsync(
        string fileName, string userId, bool isAdmin) => throw new NotSupportedException();
    public Task<AltomateHR.Api.Common.Tabular.TabularExportResult> ExportSummaryAsync(
        AltomateHR.Api.Modules.Claims.Dtos.ClaimsExportQueryDto query, AltomateHR.Api.Common.Tabular.TabularFormat format) =>
        throw new NotSupportedException();
    public AltomateHR.Api.Common.Tabular.TabularExportResult BuildImportTemplate(AltomateHR.Api.Common.Tabular.TabularFormat format) =>
        throw new NotSupportedException();
    public Task<AltomateHR.Api.Common.Tabular.TabularImportResult> ImportAsync(
        byte[] content, AltomateHR.Api.Common.Tabular.TabularFormat format) => throw new NotSupportedException();
    public Task<int> ReconcileUnreachableApprovalsAsync(bool apply) => throw new NotSupportedException();
    public Task<IReadOnlyList<AltomateHR.Api.Modules.Claims.Dtos.OrgApprovalDigestEntryDto>>
        GetOrgApprovalDigestAsync() => throw new NotSupportedException();
    public Task<AltomateHR.Api.Modules.Claims.Dtos.ClaimSettingsDto> GetSettingsAsync() =>
        throw new NotSupportedException();
    public Task<AltomateHR.Api.Modules.Claims.Dtos.ClaimSettingsDto?> UpdateSettingsAsync(
        AltomateHR.Api.Modules.Claims.Dtos.UpdateClaimSettingsDto dto) => throw new NotSupportedException();
    public Task<AltomateHR.Api.Common.Tabular.TabularExportResult> ExportPayrollReimbursementsAsync(
        AltomateHR.Api.Common.Tabular.TabularFormat format, string? month) => throw new NotSupportedException();
}
