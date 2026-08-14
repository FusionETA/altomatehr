using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Leave.Dtos;
using AltomateHR.Api.Modules.Leave.Entities;
using AltomateHR.Api.Modules.Policies;
using AltomateHR.Api.Modules.Teams;

namespace AltomateHR.Api.Modules.Leave;

// Business logic: apply, list (mine vs team), approve/reject/cancel, balances.
// Approvals route through the team's LEAVE chain (multi-step) via IApprovalRouter,
// falling back to a single supervisor step when there's no chain.
public class LeaveService : ILeaveService
{
    private const ApprovalModule Module = ApprovalModule.LEAVE;

    private readonly ILeaveApplicationRepository _apps;
    private readonly ILeaveTypeRepository _types;
    private readonly ISupervisionService _supervision;
    private readonly IPolicyService _policies;
    private readonly IApprovalRouter _router;

    public LeaveService(
        ILeaveApplicationRepository apps,
        ILeaveTypeRepository types,
        ISupervisionService supervision,
        IPolicyService policies,
        IApprovalRouter router)
    {
        _apps = apps;
        _types = types;
        _supervision = supervision;
        _policies = policies;
        _router = router;
    }

    public async Task<IEnumerable<LeaveApplicationDto>> GetMineAsync(string userId) =>
        (await _apps.GetByEmployeeAsync(userId)).Select(ToDto);

    // Applications the caller can act on right now: org approvers see the whole
    // org; otherwise only PENDING applications where the caller is an approver at
    // the application's current chain step.
    public async Task<IEnumerable<LeaveApplicationDto>> GetTeamAsync(string userId, string? role)
    {
        var all = await _apps.GetAllAsync();
        List<LeaveApplication> visible;
        if (_router.IsOrgApprover(role))
        {
            visible = all;
        }
        else
        {
            visible = [];
            foreach (var a in all.Where(a => a.Status == LeaveStatus.PENDING))
            {
                var approvers = await _router.CurrentApproversAsync(Module, a.EmployeeId, a.CurrentStep);
                if (approvers.Contains(userId)) visible.Add(a);
            }
        }

        var emails = await _supervision.GetEmailsAsync(visible.Select(a => a.EmployeeId).Distinct());
        return visible.Select(a =>
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
            TotalDays = (end - start).Days + 1,
            Reason = dto.Reason,
            Status = LeaveStatus.PENDING,
            CurrentStep = 0,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await _apps.AddAsync(application);
        return new LeaveApplyResult(true, ToDto(application), null);
    }

    public async Task<LeaveTransitionResult> ApproveAsync(string id, string approverId, string? role)
    {
        var (app, error) = await AuthorizeAsync(id, approverId, role);
        if (error is not null) return error;

        var now = DateTime.UtcNow;
        var stepCount = await _router.StepCountAsync(Module, app!.EmployeeId);
        var isFinal = _router.IsOrgApprover(role) || app.CurrentStep + 1 >= stepCount;
        if (isFinal)
        {
            app.Status = LeaveStatus.APPROVED;
            app.DecidedAt = now;
        }
        else
        {
            app.CurrentStep += 1;   // advance to the next step; stays PENDING
        }
        app.UpdatedAt = now;
        await _apps.UpdateAsync(app);
        return new LeaveTransitionResult(true, true, ToDto(app));
    }

    public async Task<LeaveTransitionResult> RejectAsync(string id, string approverId, string? role, string? reviewNotes)
    {
        var (app, error) = await AuthorizeAsync(id, approverId, role);
        if (error is not null) return error;

        var now = DateTime.UtcNow;
        app!.Status = LeaveStatus.REJECTED;
        app.ReviewNotes = reviewNotes;
        app.DecidedAt = now;
        app.UpdatedAt = now;
        await _apps.UpdateAsync(app);
        return new LeaveTransitionResult(true, true, ToDto(app));
    }

    public async Task<LeaveTransitionResult> CancelAsync(string id, string userId)
    {
        var application = await _apps.GetByIdAsync(id);
        if (application is null || application.EmployeeId != userId)
            return new LeaveTransitionResult(false, false, null);

        if (application.Status != LeaveStatus.PENDING)
            return new LeaveTransitionResult(true, false, ToDto(application),
                "Only pending applications can be cancelled.");

        application.Status = LeaveStatus.CANCELLED;
        application.UpdatedAt = DateTime.UtcNow;
        await _apps.UpdateAsync(application);
        return new LeaveTransitionResult(true, true, ToDto(application));
    }

    // Loads the app and checks the caller may act at its current step. Returns
    // an error result (to return as-is) on any failure; otherwise the app.
    private async Task<(LeaveApplication? App, LeaveTransitionResult? Error)> AuthorizeAsync(
        string id, string approverId, string? role)
    {
        var app = await _apps.GetByIdAsync(id);
        if (app is null)
            return (null, new LeaveTransitionResult(false, false, null));

        if (!_router.IsOrgApprover(role))
        {
            var approvers = await _router.CurrentApproversAsync(Module, app.EmployeeId, app.CurrentStep);
            if (!approvers.Contains(approverId))
                return (null, new LeaveTransitionResult(false, false, null));   // not the current approver → hide
        }

        if (app.Status != LeaveStatus.PENDING)
            return (app, new LeaveTransitionResult(true, false, ToDto(app),
                "Only pending applications can be approved or rejected."));

        return (app, null);
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
