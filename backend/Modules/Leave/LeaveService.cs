using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Leave.Dtos;
using AltomateHR.Api.Modules.Leave.Entities;
using AltomateHR.Api.Modules.Policies;

namespace AltomateHR.Api.Modules.Leave;

// Business logic: apply, list (mine vs team), approve/reject/cancel, balances.
// Approvals are routed: a supervisor may only act on their direct reports'
// applications; admins/owners may act on any (ISupervisionService decides).
public class LeaveService : ILeaveService
{
    private readonly ILeaveApplicationRepository _apps;
    private readonly ILeaveTypeRepository _types;
    private readonly ISupervisionService _supervision;
    private readonly IPolicyService _policies;

    public LeaveService(
        ILeaveApplicationRepository apps,
        ILeaveTypeRepository types,
        ISupervisionService supervision,
        IPolicyService policies)
    {
        _apps = apps;
        _types = types;
        _supervision = supervision;
        _policies = policies;
    }

    // The caller's own applications.
    public async Task<IEnumerable<LeaveApplicationDto>> GetMineAsync(string userId) =>
        (await _apps.GetByEmployeeAsync(userId)).Select(ToDto);

    // Applications the caller can act on: an org approver sees the whole org; a
    // supervisor sees only their direct reports; everyone else sees nothing.
    // Each row is labelled with the applicant's email so the approver knows who filed it.
    public async Task<IEnumerable<LeaveApplicationDto>> GetTeamAsync(string userId, string? role)
    {
        List<LeaveApplication> apps;
        if (_supervision.IsOrgApprover(role))
        {
            apps = await _apps.GetAllAsync();
        }
        else
        {
            var reports = (await _supervision.GetReportIdsAsync(userId)).ToHashSet();
            if (reports.Count == 0) return [];
            apps = (await _apps.GetAllAsync()).Where(a => reports.Contains(a.EmployeeId)).ToList();
        }

        var emails = await _supervision.GetEmailsAsync(apps.Select(a => a.EmployeeId).Distinct());
        return apps.Select(a =>
        {
            var dto = ToDto(a);
            dto.EmployeeEmail = emails.GetValueOrDefault(a.EmployeeId);
            return dto;
        });
    }

    public async Task<IEnumerable<LeaveBalanceDto>> GetBalancesAsync(string employeeId)
    {
        var types = (await _types.GetAllAsync()).Where(t => !t.IsArchived).ToList();
        var apps = await _apps.GetByEmployeeAsync(employeeId);
        // Per-policy entitlement overrides; fall back to the leave type's default.
        var overrides = await _policies.GetLeaveEntitlementsAsync(employeeId);
        var year = DateTime.UtcNow.Year;

        return types.Select(t =>
        {
            var entitlement = overrides.GetValueOrDefault(t.Id, t.DefaultDays);
            var forType = apps.Where(a => a.LeaveTypeId == t.Id && a.StartDate.Year == year).ToList();
            var taken = forType.Where(a => a.Status == LeaveStatus.APPROVED).Sum(a => a.TotalDays);
            var pending = forType.Where(a => a.Status == LeaveStatus.PENDING).Sum(a => a.TotalDays);
            return new LeaveBalanceDto
            {
                LeaveTypeId = t.Id,
                Code = t.Code,
                Name = t.Name,
                Paid = t.Paid,
                EntitlementDays = entitlement,
                TakenDays = taken,
                PendingDays = pending,
                RemainingDays = entitlement - taken,
            };
        });
    }

    public async Task<LeaveApplyResult> ApplyAsync(CreateLeaveApplicationDto dto, string employeeId)
    {
        var type = await _types.GetByIdAsync(dto.LeaveTypeId);
        if (type is null || type.IsArchived)
            return new LeaveApplyResult(false, null, "Pick a valid leave type.");

        var start = dto.StartDate!.Value.Date;
        var end = dto.EndDate!.Value.Date;
        if (end < start)
            return new LeaveApplyResult(false, null, "The end date can't be before the start date.");

        var now = DateTime.UtcNow;
        var application = new LeaveApplication
        {
            EmployeeId = employeeId,
            LeaveTypeId = type.Id,
            StartDate = start,
            EndDate = end,
            TotalDays = (end - start).Days + 1,   // inclusive calendar span
            Reason = dto.Reason,
            Status = LeaveStatus.PENDING,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await _apps.AddAsync(application);
        return new LeaveApplyResult(true, ToDto(application), null);
    }

    public Task<LeaveTransitionResult> ApproveAsync(string id, string approverId, string? role) =>
        TransitionAsync(id, approverId, role, LeaveStatus.APPROVED, reviewNotes: null);

    public Task<LeaveTransitionResult> RejectAsync(string id, string approverId, string? role, string? reviewNotes) =>
        TransitionAsync(id, approverId, role, LeaveStatus.REJECTED, reviewNotes);

    public async Task<LeaveTransitionResult> CancelAsync(string id, string userId)
    {
        var application = await _apps.GetByIdAsync(id);
        if (application is null || application.EmployeeId != userId)
            return new LeaveTransitionResult(false, false, null);   // not yours → 404

        if (application.Status != LeaveStatus.PENDING)
            return new LeaveTransitionResult(true, false, ToDto(application),
                "Only pending applications can be cancelled.");

        application.Status = LeaveStatus.CANCELLED;
        application.UpdatedAt = DateTime.UtcNow;
        await _apps.UpdateAsync(application);
        return new LeaveTransitionResult(true, true, ToDto(application));
    }

    // Approve/reject: authorise (supervisor of the applicant, or org approver),
    // then transition. Unauthorised is treated as not-found so the app is hidden.
    private async Task<LeaveTransitionResult> TransitionAsync(
        string id, string approverId, string? role, LeaveStatus next, string? reviewNotes)
    {
        var application = await _apps.GetByIdAsync(id);
        if (application is null)
            return new LeaveTransitionResult(false, false, null);

        if (!await _supervision.CanApproveAsync(application.EmployeeId, approverId, role))
            return new LeaveTransitionResult(false, false, null);

        if (application.Status != LeaveStatus.PENDING)
            return new LeaveTransitionResult(true, false, ToDto(application),
                "Only pending applications can be approved or rejected.");

        var now = DateTime.UtcNow;
        application.Status = next;
        application.ReviewNotes = reviewNotes;
        application.DecidedAt = now;
        application.UpdatedAt = now;
        await _apps.UpdateAsync(application);
        return new LeaveTransitionResult(true, true, ToDto(application));
    }

    private static string? Iso(DateTime? d) =>
        d is null ? null : DateTime.SpecifyKind(d.Value, DateTimeKind.Utc).ToString("o");

    private static LeaveApplicationDto ToDto(LeaveApplication a) => new()
    {
        Id = a.Id,
        EmployeeId = a.EmployeeId,
        LeaveTypeId = a.LeaveTypeId,
        StartDate = a.StartDate.ToString("yyyy-MM-dd"),
        EndDate = a.EndDate.ToString("yyyy-MM-dd"),
        TotalDays = a.TotalDays,
        Reason = a.Reason,
        Status = a.Status,
        ReviewNotes = a.ReviewNotes,
        DecidedAt = Iso(a.DecidedAt),
        CreatedAt = Iso(a.CreatedAt) ?? string.Empty,
    };
}
