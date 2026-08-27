using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Leave.Dtos;
using AltomateHR.Api.Modules.Leave.Entities;
using AltomateHR.Api.Modules.Policies;
using AltomateHR.Api.Modules.Teams;
using AltomateHR.Api.Modules.Xero;

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
    private readonly IOrganizationMembershipRepository _memberships;
    private readonly ICurrentUser _currentUser;
    private readonly ILeaveEntitlementRepository _entitlements;
    private readonly IXeroService _xero;

    public LeaveService(
        ILeaveApplicationRepository apps,
        ILeaveTypeRepository types,
        ISupervisionService supervision,
        IPolicyService policies,
        IApprovalRouter router,
        IOrganizationMembershipRepository memberships,
        ICurrentUser currentUser,
        ILeaveEntitlementRepository entitlements,
        IXeroService xero)
    {
        _apps = apps;
        _types = types;
        _supervision = supervision;
        _policies = policies;
        _router = router;
        _memberships = memberships;
        _currentUser = currentUser;
        _entitlements = entitlements;
        _xero = xero;
    }

    public async Task<IEnumerable<LeaveApplicationDto>> GetMineAsync(string userId) =>
        (await _apps.GetByEmployeeAsync(userId)).Select(ToDto);

    // Applications the caller can act on right now: PENDING applications where
    // the caller is an approver at the application's current chain step. Purely
    // by team seat — a role alone (Admin/Owner) grants nothing here.
    public async Task<IEnumerable<LeaveApplicationDto>> GetTeamAsync(string userId)
    {
        var all = await _apps.GetAllAsync();
        var visible = new List<LeaveApplication>();
        foreach (var a in all.Where(a => a.Status == LeaveStatus.PENDING))
        {
            var approvers = await _router.CurrentApproversAsync(Module, a.EmployeeId, a.CurrentStep);
            if (approvers.Contains(userId)) visible.Add(a);
        }

        var emails = await _supervision.GetEmailsAsync(visible.Select(a => a.EmployeeId).Distinct());
        return visible.Select(a =>
        {
            var dto = ToDto(a);
            dto.EmployeeEmail = emails.GetValueOrDefault(a.EmployeeId);
            return dto;
        });
    }

    // Org-wide balances grid (admin). Deliberately bulk: one read for members,
    // one for types, one for applications and a per-DISTINCT-policy entitlement
    // read — not per employee. Production hit the same N+1 and solved it the
    // same way, with a bulk reader behind the grid.
    public async Task<IEnumerable<EmployeeLeaveBalancesDto>> GetOrgBalancesAsync(int year)
    {
        var members = await _memberships.GetForCurrentOrgAsync();
        if (members.Count == 0) return Array.Empty<EmployeeLeaveBalancesDto>();

        var userIds = members.Select(m => m.UserId).Distinct().ToList();
        var types = (await _types.GetAllAsync()).Where(t => !t.IsArchived).ToList();
        var appsByEmployee = (await _apps.GetAllAsync())
            .GroupBy(a => a.EmployeeId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var entitlements = await _policies.GetLeaveEntitlementsForEmployeesAsync(userIds);
        var emails = await _supervision.GetEmailsAsync(userIds);
        var rowsByEmployee = (await _entitlements.GetByYearAsync(year))
            .GroupBy(e => e.EmployeeId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(e => e.LeaveTypeId));

        return members.Select(m => new EmployeeLeaveBalancesDto
        {
            UserId = m.UserId,
            Email = emails.GetValueOrDefault(m.UserId) ?? string.Empty,
            Role = m.Role,
            Balances = BuildBalances(
                types,
                appsByEmployee.GetValueOrDefault(m.UserId) ?? new List<LeaveApplication>(),
                entitlements.GetValueOrDefault(m.UserId) ?? new Dictionary<string, double>(),
                rowsByEmployee.GetValueOrDefault(m.UserId) ?? new Dictionary<string, LeaveEntitlement>(),
                year),
        });
    }

    // Another employee's balances. Access mirrors production's
    // resolveEmployeeReportAccess: admin/owner → anyone in the org;
    // anyone → themselves; supervisor → their direct reports; else refused.
    public async Task<LeaveBalancesResult> GetBalancesForEmployeeAsync(string employeeId, int year)
    {
        var membership = await _memberships.GetForUserInCurrentOrgAsync(employeeId);
        if (membership is null)
            return new LeaveBalancesResult(false, false, Array.Empty<LeaveBalanceDto>(), year);

        if (!await CanReadBalancesAsync(employeeId))
            return new LeaveBalancesResult(true, false, Array.Empty<LeaveBalanceDto>(), year);

        var balances = await GetBalancesAsync(employeeId, year);
        return new LeaveBalancesResult(true, true, balances, year);
    }

    // Balances for ONE employee as a CSV download. Reuses the same access rule
    // as the JSON reader — an export must never be a way around it.
    public async Task<LeaveExportResult> ExportBalancesCsvAsync(string employeeId, int year)
    {
        var balances = await GetBalancesForEmployeeAsync(employeeId, year);
        if (!balances.Found || !balances.Allowed)
            return new LeaveExportResult(balances.Found, balances.Allowed, [], "");

        var emails = await _supervision.GetEmailsAsync([employeeId]);
        var label = emails.GetValueOrDefault(employeeId) ?? employeeId;

        return new LeaveExportResult(
            true, true,
            LeaveCsvExporter.BalancesToCsv(balances.Balances, label),
            $"leave-summary-{year}.csv");
    }

    // Every employee's balances in one file. Gated at the controller (Admin/Owner)
    // because it spans the whole org rather than one person.
    public async Task<LeaveExportResult> ExportOrgBalancesCsvAsync(int year)
    {
        var rows = await GetOrgBalancesAsync(year);
        return new LeaveExportResult(
            true, true,
            LeaveCsvExporter.OrgBalancesToCsv(rows),
            $"leave-summary-all-{year}.csv");
    }

    // Streams a leave attachment out of Xero Files. The id alone proves nothing,
    // so it's resolved back to its leave application first and the caller is
    // checked against THAT — an attachment is only as visible as its request.
    //
    // Every refusal is a 404, including "file exists but isn't yours": a 403
    // would confirm the file exists.
    public async Task<LeaveAttachmentResult> GetAttachmentAsync(string xeroFileId)
    {
        var application = await _apps.GetByXeroFileIdAsync(xeroFileId);
        if (application is null) return NotFoundAttachment;

        if (!await CanReadBalancesAsync(application.EmployeeId)) return NotFoundAttachment;

        var file = await _xero.GetFileContentAsync(xeroFileId);
        return file is null
            ? NotFoundAttachment
            : new LeaveAttachmentResult(true, file.Content, file.ContentType, file.FileName);
    }

    private static readonly LeaveAttachmentResult NotFoundAttachment = new(false, [], "", "");

    private async Task<bool> CanReadBalancesAsync(string employeeId)
    {
        var callerId = _currentUser.UserId;
        if (callerId is null) return false;
        if (callerId == employeeId) return true;                       // your own
        if (_supervision.IsOrgApprover(_currentUser.Role)) return true; // admin/owner
        var reports = await _supervision.GetReportIdsAsync(callerId);   // your team
        return reports.Contains(employeeId);
    }

    // One employee's per-type balances for a given year. Callers that need the
    // org-membership check first should use GetBalancesForEmployeeAsync.
    // One employee's per-type balances for a given year. Callers that need the
    // org-membership check first should use GetBalancesForEmployeeAsync.
    public async Task<IEnumerable<LeaveBalanceDto>> GetBalancesAsync(string employeeId, int year)
    {
        var types = (await _types.GetAllAsync()).Where(t => !t.IsArchived).ToList();
        var apps = await _apps.GetByEmployeeAsync(employeeId);
        var overrides = await _policies.GetLeaveEntitlementsAsync(employeeId);
        var rows = (await _entitlements.GetForEmployeeYearAsync(employeeId, year))
            .ToDictionary(e => e.LeaveTypeId);
        return BuildBalances(types, apps, overrides, rows, year);
    }

    // Pure projection shared by the single-employee and org-wide readers, so the
    // balance rules live in ONE place.
    //
    // Prefers the STORED entitlement row (what the crons maintain: accrual and
    // carry-forward). Falls back to the policy/type default when the year hasn't
    // been opened yet — that projection is what the rollover would create, and
    // IsOpened=false tells the caller it isn't stored state.
    private static List<LeaveBalanceDto> BuildBalances(
        IEnumerable<LeaveType> types,
        IReadOnlyCollection<LeaveApplication> apps,
        IReadOnlyDictionary<string, double> overrides,
        IReadOnlyDictionary<string, LeaveEntitlement> rows,
        int year) =>
        types.Select(t =>
        {
            var forType = apps.Where(a => a.LeaveTypeId == t.Id && a.StartDate.Year == year).ToList();
            var taken = forType.Where(a => a.Status == LeaveStatus.APPROVED).Sum(a => a.TotalDays);
            var pending = forType.Where(a => a.Status == LeaveStatus.PENDING).Sum(a => a.TotalDays);

            rows.TryGetValue(t.Id, out var row);
            var opened = row is not null;

            var entitled = row?.EntitledDays ?? overrides.GetValueOrDefault(t.Id, t.DefaultDays);
            var method = row?.AccrualMethod ?? t.AccrualMethod;
            // An unopened year projects what the rollover would seed: the whole
            // entitlement for LUMP_SUM, nothing yet for PRO_RATED.
            var accrued = row?.AccruedDays
                          ?? (method == LeaveAccrualMethod.PRO_RATED ? 0 : entitled);
            var carried = row?.CarriedDays ?? 0;
            var carriedExpired = row?.CarriedExpired ?? false;

            return new LeaveBalanceDto
            {
                LeaveTypeId = t.Id,
                Code = t.Code,
                Name = t.Name,
                Paid = t.Paid,
                Year = year,
                IsOpened = opened,
                AccrualMethod = method.ToString(),
                EntitlementDays = entitled,
                AccruedDays = accrued,
                CarriedDays = carried,
                CarriedExpiresAt = row?.CarriedExpiresAt,
                CarriedExpired = carriedExpired,
                TakenDays = taken,
                PendingDays = pending,
                RemainingDays = LeaveAccrualMath.AvailableDays(
                    method, entitled, accrued, carried, carriedExpired, taken),
            };
        }).ToList();

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

    public async Task<LeaveTransitionResult> ApproveAsync(string id, string approverId)
    {
        var (app, error) = await AuthorizeAsync(id, approverId);
        if (error is not null) return error;

        var now = DateTime.UtcNow;
        var stepCount = await _router.StepCountAsync(Module, app!.EmployeeId);
        var isFinal = app.CurrentStep + 1 >= stepCount;
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

    public async Task<LeaveTransitionResult> RejectAsync(string id, string approverId, string? reviewNotes)
    {
        var (app, error) = await AuthorizeAsync(id, approverId);
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
        string id, string approverId)
    {
        var app = await _apps.GetByIdAsync(id);
        if (app is null)
            return (null, new LeaveTransitionResult(false, false, null));

        var approvers = await _router.CurrentApproversAsync(Module, app.EmployeeId, app.CurrentStep);
        if (!approvers.Contains(approverId))
            return (null, new LeaveTransitionResult(false, false, null));   // not the current approver → hide

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
