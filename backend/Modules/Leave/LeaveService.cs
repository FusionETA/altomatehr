using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Leave.Dtos;
using AltomateHR.Api.Modules.Leave.Entities;
using AltomateHR.Api.Modules.Organizations;
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
    private readonly IOrganizationService _organizations;

    public LeaveService(
        ILeaveApplicationRepository apps,
        ILeaveTypeRepository types,
        ISupervisionService supervision,
        IPolicyService policies,
        IApprovalRouter router,
        IOrganizationMembershipRepository memberships,
        ICurrentUser currentUser,
        ILeaveEntitlementRepository entitlements,
        IXeroService xero,
        IOrganizationService organizations)
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
        _organizations = organizations;
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
    // Ported from production's buildEmployeeSection. Two rules worth keeping
    // visible: a request is bucketed entirely into its START month (a
    // 28 Dec - 3 Jan request counts as December), and Balance is
    // Entitled + Carried - Total, deliberately NOT accrued-based.
    public async Task<LeaveSummaryReportResult> GetSummaryReportAsync(string employeeId, int year)
    {
        var membership = await _memberships.GetForUserInCurrentOrgAsync(employeeId);
        if (membership is null) return new LeaveSummaryReportResult(false, false, null);
        if (!await CanReadBalancesAsync(employeeId))
            return new LeaveSummaryReportResult(true, false, null);

        var typesById = (await _types.GetAllAsync()).ToDictionary(t => t.Id);
        var rows = await _entitlements.GetForEmployeeYearAsync(employeeId, year);
        var approved = (await _apps.GetByEmployeeAsync(employeeId))
            .Where(a => a.Status == LeaveStatus.APPROVED && a.StartDate.Year == year)
            .ToList();

        // leaveTypeId → 12 months of days taken.
        var usage = new Dictionary<string, double[]>();
        foreach (var a in approved)
        {
            if (!usage.TryGetValue(a.LeaveTypeId, out var months))
                usage[a.LeaveTypeId] = months = new double[12];
            months[a.StartDate.Month - 1] += a.TotalDays;
        }

        var monthlyRows = rows.Select(ent =>
        {
            var months = usage.GetValueOrDefault(ent.LeaveTypeId) ?? new double[12];
            var total = months.Sum();
            return new LeaveMonthlyRowDto
            {
                LeaveTypeName = typesById.GetValueOrDefault(ent.LeaveTypeId)?.Name ?? ent.LeaveTypeId,
                EntitledDays = ent.EntitledDays,
                CarriedDays = ent.CarriedDays,
                Monthly = months.Select(v => v == 0 ? (double?)null : v).ToList(),
                Total = total,
                Balance = ent.EntitledDays + ent.CarriedDays - total,
            };
        }).ToList();

        var detailRows = approved
            .OrderBy(a => a.StartDate)
            .Select(a => new LeaveDetailRowDto
            {
                From = a.StartDate,
                To = a.EndDate,
                LeaveTypeName = typesById.GetValueOrDefault(a.LeaveTypeId)?.Name ?? a.LeaveTypeId,
                Days = a.TotalDays,
                Reason = a.Reason,
                // Production shows the attachment's filename; V2 stores only the
                // Xero file id, so that is what surfaces until AttachmentName exists.
                AttachmentName = a.XeroFileId,
            })
            .ToList();

        var emails = await _supervision.GetEmailsAsync([employeeId]);
        var org = _currentUser.OrganizationId is { } orgId
            ? await _organizations.GetByIdAsync(orgId)
            : null;

        return new LeaveSummaryReportResult(true, true, new LeaveSummaryReportDto
        {
            OrganizationName = org?.Name ?? "Organization",
            EmployeeLabel = emails.GetValueOrDefault(employeeId) ?? employeeId,
            Year = year,
            ReportDate = DateTime.UtcNow,
            MonthlyRows = monthlyRows,
            DetailRows = detailRows,
        });
    }

    public async Task<LeaveExportResult> ExportSummaryPdfAsync(string employeeId, int year)
    {
        var report = await GetSummaryReportAsync(employeeId, year);
        if (!report.Found || !report.Allowed)
            return new LeaveExportResult(report.Found, report.Allowed, [], "");

        return new LeaveExportResult(true, true,
            LeaveSummaryPdf.Render(report.Report!),
            $"leave-summary-{year}.pdf");
    }

    // Override ONE employee's entitlement for a year. Production recomputes a
    // PRO_RATED row's accrued days from the JOIN DATE here; V2 has no join
    // date, so a pro-rated row keeps its accrued progress capped at the new
    // entitlement instead. Documented divergence, not an oversight.
    public async Task<LeaveEntitlementResult> SetEntitlementAsync(
        string employeeId, string leaveTypeId, int year, SetEntitlementDto dto)
    {
        if (dto.EntitledDays < 0)
            return new LeaveEntitlementResult(false, null, "Entitled days cannot be negative");

        var type = await _types.GetByIdAsync(leaveTypeId);
        if (type is null) return new LeaveEntitlementResult(false, null, "Leave type not found");

        var membership = await _memberships.GetForUserInCurrentOrgAsync(employeeId);
        if (membership is null) return new LeaveEntitlementResult(false, null, null);   // → 404

        var row = await EnsureEntitlementAsync(employeeId, type, year);
        row.EntitledDays = dto.EntitledDays;
        row.AccrualMethod = dto.AccrualMethod;

        var effective = dto.AccrualMethod ?? type.AccrualMethod;
        row.AccruedDays = effective == LeaveAccrualMethod.PRO_RATED
            ? Math.Min(row.AccruedDays, dto.EntitledDays)   // keep progress, capped
            : dto.EntitledDays;                             // LUMP_SUM: credit in full
        row.UpdatedAt = DateTime.UtcNow;
        await _entitlements.SaveAsync();

        var balance = (await GetBalancesAsync(employeeId, year))
            .FirstOrDefault(b => b.LeaveTypeId == leaveTypeId);
        return new LeaveEntitlementResult(true, balance, null);
    }

    // Clears the override: back to the policy value, else the type default.
    public async Task<LeaveEntitlementResult> ResetEntitlementAsync(
        string employeeId, string leaveTypeId, int year)
    {
        var type = await _types.GetByIdAsync(leaveTypeId);
        if (type is null) return new LeaveEntitlementResult(false, null, "Leave type not found");

        var overrides = await _policies.GetLeaveEntitlementsAsync(employeeId);
        var days = overrides.GetValueOrDefault(leaveTypeId, type.DefaultDays);

        return await SetEntitlementAsync(employeeId, leaveTypeId, year,
            new SetEntitlementDto { EntitledDays = days, AccrualMethod = null });
    }

    // Opens the year for one employee — the per-person half of the rollover,
    // for someone who joins after the cron has already run.
    public async Task<int> SeedEntitlementsAsync(string employeeId, int year)
    {
        var created = 0;
        foreach (var type in (await _types.GetAllAsync()).Where(t => !t.IsArchived))
        {
            var before = (await _entitlements.GetForEmployeeYearAsync(employeeId, year))
                .Any(e => e.LeaveTypeId == type.Id);
            if (before) continue;
            await EnsureEntitlementAsync(employeeId, type, year);
            created++;
        }
        return created;
    }

    // Approved leave days overlapping a range — payroll and reporting ask this.
    public async Task<double> GetApprovedDaysInRangeAsync(string employeeId, DateTime from, DateTime to)
    {
        var start = from.Date;
        var end = to.Date;
        return (await _apps.GetByEmployeeAsync(employeeId))
            .Where(a => a.Status == LeaveStatus.APPROVED
                        && a.StartDate.Date <= end && a.EndDate.Date >= start)
            .Sum(a => a.TotalDays);
    }

    // Org dashboard: status totals, days used per type, who's out, and the
    // most recent requests.
    public async Task<LeaveOverviewDto> GetOverviewAsync(int year)
    {
        var types = (await _types.GetAllAsync()).ToDictionary(t => t.Id);
        var all = (await _apps.GetAllAsync())
            .Where(a => a.StartDate.Year == year)
            .ToList();

        var emails = await _supervision.GetEmailsAsync(all.Select(a => a.EmployeeId).Distinct());

        return new LeaveOverviewDto
        {
            Year = year,
            Totals = new LeaveStatusTotalsDto
            {
                Pending = all.Count(a => a.Status == LeaveStatus.PENDING),
                Approved = all.Count(a => a.Status == LeaveStatus.APPROVED),
                Rejected = all.Count(a => a.Status == LeaveStatus.REJECTED),
                Cancelled = all.Count(a => a.Status == LeaveStatus.CANCELLED),
            },
            DaysUsedByType = types.Values.Select(t => new LeaveDaysByTypeDto
            {
                LeaveTypeId = t.Id,
                Code = t.Code,
                Name = t.Name,
                Paid = t.Paid,
                DaysUsed = all.Where(a => a.LeaveTypeId == t.Id && a.Status == LeaveStatus.APPROVED)
                              .Sum(a => a.TotalDays),
            }).ToList(),
            OnLeaveToday = await GetOnLeaveTodayAsync(DateTime.UtcNow),
            RecentApplications = all
                .OrderByDescending(a => a.CreatedAt)
                .Take(10)
                .Select(a =>
                {
                    var dto = ToDto(a);
                    dto.EmployeeEmail = emails.GetValueOrDefault(a.EmployeeId);
                    return dto;
                })
                .ToList(),
        };
    }

    // Balances for the caller's direct reports only — the supervisor view of
    // the admin grid. Reuses the same bulk readers, so it stays one flat set
    // of queries rather than one per report.
    public async Task<IEnumerable<EmployeeLeaveBalancesDto>> GetTeamBalancesAsync(
        string supervisorId, int year)
    {
        var reportIds = (await _supervision.GetReportIdsAsync(supervisorId)).ToHashSet();
        if (reportIds.Count == 0) return Array.Empty<EmployeeLeaveBalancesDto>();

        return (await GetOrgBalancesAsync(year))
            .Where(r => reportIds.Contains(r.UserId));
    }

    // Who is out on APPROVED leave on `today` — the admin dashboard panel.
    public async Task<IEnumerable<OnLeaveTodayDto>> GetOnLeaveTodayAsync(DateTime today)
    {
        var day = today.Date;
        var typesById = (await _types.GetAllAsync()).ToDictionary(t => t.Id);

        var out_ = (await _apps.GetAllAsync())
            .Where(a => a.Status == LeaveStatus.APPROVED
                        && a.StartDate.Date <= day && day <= a.EndDate.Date)
            .ToList();

        var emails = await _supervision.GetEmailsAsync(out_.Select(a => a.EmployeeId).Distinct());

        return out_.Select(a =>
        {
            typesById.TryGetValue(a.LeaveTypeId, out var type);
            return new OnLeaveTodayDto
            {
                EmployeeId = a.EmployeeId,
                Email = emails.GetValueOrDefault(a.EmployeeId),
                LeaveTypeId = a.LeaveTypeId,
                LeaveTypeCode = type?.Code ?? string.Empty,
                LeaveTypeName = type?.Name ?? string.Empty,
                StartDate = a.StartDate,
                EndDate = a.EndDate,
                TotalDays = a.TotalDays,
            };
        });
    }

    // Count only — the approval badge shouldn't have to pull the whole queue.
    public async Task<int> CountPendingApprovalsAsync(string reviewerId) =>
        (await GetTeamAsync(reviewerId)).Count();

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

    // Order mirrors production's submitLeaveApplication: shape checks, then
    // ensure the entitlement row exists, then the balance check — which is
    // SKIPPED for unpaid types (usage is still tracked, negative is allowed).
    public async Task<LeaveApplyResult> ApplyAsync(CreateLeaveApplicationDto dto, string employeeId)
    {
        var type = await _types.GetByIdAsync(dto.LeaveTypeId);
        if (type is null) return new LeaveApplyResult(false, null, "Leave type not found");
        if (type.IsArchived) return new LeaveApplyResult(false, null, "Leave type is archived");

        var start = dto.StartDate!.Value.Date;
        var end = dto.EndDate!.Value.Date;
        if (end < start)
            return new LeaveApplyResult(false, null, "End date is before start date");

        // NOTE: production counts WORKING days here (computeTotalDays), skipping
        // non-working weekdays and public holidays. V2 has neither an org
        // working-days setting nor a holiday calendar yet, so this still counts
        // calendar days — tracked as a known divergence.
        var totalDays = (end - start).Days + 1;
        if (totalDays <= 0)
            return new LeaveApplyResult(false, null, "Selected dates contain no working days");

        var year = start.Year;
        var entitlement = await EnsureEntitlementAsync(employeeId, type, year);

        if (type.Paid)
        {
            var available = await AvailableForApplyAsync(employeeId, type, entitlement, year);
            if (totalDays > available + Tolerance)
            {
                var rounded = Math.Round(available * 100) / 100;
                return new LeaveApplyResult(false, null,
                    $"Insufficient balance: requesting {totalDays} but only {rounded} available");
            }
        }

        var now = DateTime.UtcNow;
        var application = new LeaveApplication
        {
            EmployeeId = employeeId,
            LeaveTypeId = type.Id,
            StartDate = start,
            EndDate = end,
            TotalDays = totalDays,
            Reason = dto.Reason,
            Status = LeaveStatus.PENDING,
            CurrentStep = 0,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await _apps.AddAsync(application);
        return new LeaveApplyResult(true, ToDto(application), null);
    }

    // Edit a pending request. Rules ported from production's
    // editLeaveApplication: your own leave only, still pending, and untouched
    // by an approver — re-editing after someone has reviewed a step would
    // silently change what they approved.
    public async Task<LeaveApplyResult> EditAsync(
        string id, CreateLeaveApplicationDto dto, string actorUserId)
    {
        var app = await _apps.GetByIdAsync(id);
        if (app is null) return new LeaveApplyResult(false, null, "Application not found");
        if (app.EmployeeId != actorUserId)
            return new LeaveApplyResult(false, null, "You can only edit your own leave");
        if (app.Status != LeaveStatus.PENDING)
            return new LeaveApplyResult(false, null, "Only pending leave can be edited");

        // Production inspects the `approvals` JSON trail. V2 has no such column
        // yet, so CurrentStep > 0 stands in: the chain only advances once an
        // approver has acted. Same intent, coarser evidence.
        if (app.CurrentStep > 0)
            return new LeaveApplyResult(false, null,
                "Cannot edit — an approver has already reviewed this leave");

        var type = await _types.GetByIdAsync(dto.LeaveTypeId);
        if (type is null) return new LeaveApplyResult(false, null, "Leave type not found");
        if (type.IsArchived) return new LeaveApplyResult(false, null, "Leave type is archived");

        var start = dto.StartDate!.Value.Date;
        var end = dto.EndDate!.Value.Date;
        if (end < start)
            return new LeaveApplyResult(false, null, "End date is before start date");

        var totalDays = (end - start).Days + 1;   // see ApplyAsync: calendar days for now
        if (totalDays <= 0)
            return new LeaveApplyResult(false, null, "Selected dates contain no working days");

        var year = start.Year;
        var entitlement = await EnsureEntitlementAsync(actorUserId, type, year);

        if (type.Paid)
        {
            // Exclude THIS request from the pending total, or an edit would be
            // checked against a balance its own days are already reserving.
            var available = await AvailableForApplyAsync(
                actorUserId, type, entitlement, year, excludeApplicationId: id);
            if (totalDays > available + Tolerance)
            {
                var rounded = Math.Round(available * 100) / 100;
                return new LeaveApplyResult(false, null,
                    $"Insufficient balance: requesting {totalDays} but only {rounded} available");
            }
        }

        app.LeaveTypeId = type.Id;
        app.StartDate = start;
        app.EndDate = end;
        app.TotalDays = totalDays;
        app.Reason = dto.Reason;
        app.UpdatedAt = DateTime.UtcNow;
        await _apps.UpdateAsync(app);
        return new LeaveApplyResult(true, ToDto(app), null);
    }

    // Rows are normally created by the year-rollover cron, but an employee may
    // apply before it has run for that year — so create on demand, exactly as
    // production's ensureEntitlement does.
    private async Task<LeaveEntitlement> EnsureEntitlementAsync(
        string employeeId, LeaveType type, int year)
    {
        var existing = (await _entitlements.GetForEmployeeYearAsync(employeeId, year))
            .FirstOrDefault(e => e.LeaveTypeId == type.Id);
        if (existing is not null) return existing;

        var overrides = await _policies.GetLeaveEntitlementsAsync(employeeId);
        var entitled = overrides.GetValueOrDefault(type.Id, type.DefaultDays);
        var now = DateTime.UtcNow;

        var row = new LeaveEntitlement
        {
            EmployeeId = employeeId,
            LeaveTypeId = type.Id,
            Year = year,
            EntitledDays = entitled,
            // PRO_RATED fills monthly via the cron; LUMP_SUM is whole up front.
            AccruedDays = type.AccrualMethod == LeaveAccrualMethod.PRO_RATED ? 0 : entitled,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await _entitlements.AddAsync(row);
        await _entitlements.SaveAsync();
        return row;
    }

    // Days the employee may book right now. Approved days are already counted
    // by AvailableDays; PENDING days are subtracted too, otherwise three
    // simultaneous requests could each pass the check on the same balance.
    private const double Tolerance = 0.0001;

    private async Task<double> AvailableForApplyAsync(
        string employeeId, LeaveType type, LeaveEntitlement row, int year,
        string? excludeApplicationId = null)
    {
        var apps = (await _apps.GetByEmployeeAsync(employeeId))
            .Where(a => a.LeaveTypeId == type.Id && a.StartDate.Year == year)
            .Where(a => excludeApplicationId is null || a.Id != excludeApplicationId)
            .ToList();

        var taken = apps.Where(a => a.Status == LeaveStatus.APPROVED).Sum(a => a.TotalDays);
        var pending = apps.Where(a => a.Status == LeaveStatus.PENDING).Sum(a => a.TotalDays);

        var method = row.AccrualMethod ?? type.AccrualMethod;
        var available = LeaveAccrualMath.AvailableDays(
            method, row.EntitledDays, row.AccruedDays, row.CarriedDays, row.CarriedExpired, taken);

        return Math.Max(0, available - pending);
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

    // Production cancels PENDING *and* APPROVED requests — cancelling an
    // approved one gives the days back. Only REJECTED can't be cancelled, and
    // cancelling an already-cancelled request is a no-op success.
    public async Task<LeaveTransitionResult> CancelAsync(string id, string userId)
    {
        var application = await _apps.GetByIdAsync(id);
        if (application is null)
            return new LeaveTransitionResult(false, false, null, "Application not found");

        if (application.EmployeeId != userId)
            return new LeaveTransitionResult(true, false, null, "Only the applicant can cancel");

        if (application.Status == LeaveStatus.CANCELLED)
            return new LeaveTransitionResult(true, true, ToDto(application));   // idempotent

        if (application.Status == LeaveStatus.REJECTED)
            return new LeaveTransitionResult(true, false, ToDto(application),
                "Only pending or approved leave can be cancelled");

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
            return (null, new LeaveTransitionResult(false, false, null, "Application not found"));

        if (app.Status != LeaveStatus.PENDING)
            return (app, new LeaveTransitionResult(true, false, ToDto(app),
                "Application is not pending"));

        var approvers = await _router.CurrentApproversAsync(Module, app.EmployeeId, app.CurrentStep);
        if (!approvers.Contains(approverId))
            return (null, new LeaveTransitionResult(true, false, null,
                "You are not authorized to review this step"));

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
