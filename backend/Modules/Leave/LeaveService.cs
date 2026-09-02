using System.Text.Json;
using System.IO.Compression;
using AltomateHR.Api.Common;
using AltomateHR.Api.Common.Tabular;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Leave.Dtos;
using AltomateHR.Api.Modules.Leave.Entities;
using AltomateHR.Api.Modules.Holidays;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Policies;
using AltomateHR.Api.Modules.Realtime;
using AltomateHR.Api.Modules.Realtime.Dtos;
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
    private readonly IHolidayService _holidays;
    private readonly IRealtimeService _realtime;
    private readonly IEmployeeDirectory _employees;

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
        IOrganizationService organizations,
        IHolidayService holidays,
        IRealtimeService realtime,
        IEmployeeDirectory employees)
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
        _holidays = holidays;
        _realtime = realtime;
        _employees = employees;
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

    // Balances for ONE employee as a spreadsheet. Reuses the same access rule
    // as the JSON reader — an export must never be a way around it.
    public async Task<LeaveExportResult> ExportBalancesAsync(
        string employeeId, int year, TabularFormat format)
    {
        var balances = await GetBalancesForEmployeeAsync(employeeId, year);
        if (!balances.Found || !balances.Allowed)
            return new LeaveExportResult(balances.Found, balances.Allowed, [], "");

        var directory = await _employees.GetSnapshotAsync();
        var identity = directory.ById(employeeId);
        var label = identity?.Email
            ?? (await _supervision.GetEmailsAsync([employeeId])).GetValueOrDefault(employeeId)
            ?? employeeId;

        var sheet = LeaveBalancesSheet.BuildBalances(balances.Balances, label, identity?.Role ?? "");
        return new LeaveExportResult(
            true, true,
            TabularWriter.Write(sheet, format),
            $"leave-summary-{year}.{format.Extension()}");
    }

    // Every employee's balances in one file. Gated at the controller (Admin/Owner)
    // because it spans the whole org rather than one person.
    public async Task<LeaveExportResult> ExportOrgBalancesAsync(int year, TabularFormat format)
    {
        var rows = await GetOrgBalancesAsync(year);
        var sheet = LeaveBalancesSheet.BuildOrgBalances(rows);
        return new LeaveExportResult(
            true, true,
            TabularWriter.Write(sheet, format),
            $"leave-summary-all-{year}.{format.Extension()}");
    }

    public TabularExportResult BuildImportTemplate(TabularFormat format) =>
        TabularExportResult.From(
            LeaveBalancesSheet.BuildImportTemplate(), format, "leave-history-import-template");

    // Bulk-import historical leave applications — a migration off another system
    // (Jibble, Payroll Panda, a spreadsheet). Deliberately unlike ApplyAsync:
    //
    //   - the row's Status is honoured, so settled leave imports as APPROVED
    //     rather than landing in a supervisor's queue,
    //   - the source Days figure is TRUSTED instead of recomputed from the
    //     working-week calendar — the org's calendar today may not be the one
    //     that was in force then, and re-deriving would rewrite history,
    //   - the balance-sufficiency gate is SKIPPED: the leave already happened,
    //     and refusing to record it wouldn't un-take it,
    //   - archived leave types still resolve, so historical types work,
    //   - a request already on file for the same employee/type/exact dates is
    //     skipped, so a re-run never double-counts.
    //
    // Entitlement rows are opened as a side effect (EnsureEntitlementAsync), so
    // an imported APPROVED request shows up in the year's balances immediately —
    // `taken` is derived from approved applications, not stored separately.
    public async Task<TabularImportResult> ImportHistoryAsync(
        byte[] content, TabularFormat format, string adminUserId)
    {
        IReadOnlyList<IReadOnlyList<string>> rows;
        try
        {
            rows = TabularReader.Read(content, format);
        }
        catch (InvalidDataException ex)
        {
            return TabularImportResult.FileError(ex.Message);
        }

        if (rows.Count == 0) return TabularImportResult.FileError("The file is empty.");

        var columns = LeaveBalancesSheet.ImportColumns;
        var (map, missing) = TabularHeaderMap.Build(
            rows[0], columns, EmployeeImportColumns.IdentityGroup);
        if (map is null)
            return TabularImportResult.FileError($"Missing required column(s): {string.Join(", ", missing)}.");
        if (rows.Count == 1)
            return TabularImportResult.FileError("The file has a header row but no data rows.");

        var result = new TabularImportResult();
        var directory = await _employees.GetSnapshotAsync();

        // Archived types included on purpose — a type the org retired last year
        // is exactly what a year of history refers to.
        var allTypes = await _types.GetAllAsync();
        var typesByKey = new Dictionary<string, LeaveType>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in allTypes)
        {
            typesByKey.TryAdd(type.Name.Trim(), type);
            typesByKey.TryAdd(type.Code.Trim(), type);
        }

        var seen = (await _apps.GetAllAsync())
            .Select(a => HistoryKey(a.EmployeeId, a.LeaveTypeId, a.StartDate, a.EndDate))
            .ToHashSet(StringComparer.Ordinal);

        var now = DateTime.UtcNow;
        var imported = 0;

        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowNumber = i + 1;

            if (TabularTemplate.IsExampleRow(map, row, columns))
            {
                result.CountSkipped();
                continue;
            }

            var email = map.Cell(row, "employeeEmail");
            var name = map.Cell(row, "employeeName");
            var (employeeId, ambiguous) = directory.Resolve(email, name);
            if (ambiguous)
            {
                result.Fail(rowNumber, $"More than one employee is named '{name}'. Use the Employee Email column.");
                continue;
            }
            if (employeeId is null)
            {
                result.Fail(rowNumber, $"No employee in this organization matches '{(email.Length > 0 ? email : name)}'.");
                continue;
            }

            var typeCell = map.Cell(row, "leaveType").Trim();
            if (!typesByKey.TryGetValue(typeCell, out var leaveType))
            {
                result.Fail(rowNumber, $"No leave type matches '{typeCell}' (try its name or code).");
                continue;
            }

            var start = TabularCell.Date(map.Cell(row, "startDate"));
            var end = TabularCell.Date(map.Cell(row, "endDate"));
            if (start is null || end is null)
            {
                result.Fail(rowNumber, "Start Date and End Date must both be dates, e.g. 2026-01-15.");
                continue;
            }
            if (end < start)
            {
                result.Fail(rowNumber, "End Date is before Start Date.");
                continue;
            }

            var days = TabularCell.Number(map.Cell(row, "days"));
            if (days is null || days <= 0)
            {
                result.Fail(rowNumber, "Days must be a number greater than zero.");
                continue;
            }

            var status = TabularCell.Enum<LeaveStatus>(map.Cell(row, "status"));
            if (status is null)
            {
                result.Fail(rowNumber,
                    $"Status must be one of: {string.Join(", ", Enum.GetNames<LeaveStatus>())}.");
                continue;
            }

            var key = HistoryKey(employeeId, leaveType.Id, start.Value, end.Value);
            if (seen.Contains(key))
            {
                result.CountSkipped();
                continue;
            }

            // Open the year for this employee/type if the rollover never did,
            // otherwise the imported days would have no entitlement row to sit
            // against and the balance would read as "not opened".
            await EnsureEntitlementAsync(employeeId, leaveType, start.Value.Year);

            var reason = TabularCell.Text(map.Cell(row, "reason"));
            var application = new LeaveApplication
            {
                EmployeeId = employeeId,
                LeaveTypeId = leaveType.Id,
                StartDate = start.Value,
                EndDate = end.Value,
                // Half-days can't be expressed by a date range, so the source
                // Days figure carries the fraction and Duration stays FULL_DAY.
                Duration = LeaveDuration.FULL_DAY,
                TotalDays = days.Value,
                Reason = reason,
                Status = status.Value,
                CurrentStep = 0,
                AppliedByAdminId = adminUserId,
                DecidedAt = status.Value == LeaveStatus.PENDING ? null : now,
                CreatedAt = now,
                UpdatedAt = now,
            };

            // Records who really created it, so the audit trail doesn't claim
            // the employee filed it themselves.
            AppendTrail(application, 0, adminUserId, "IMPORTED", reason);

            await _apps.AddAsync(application);
            seen.Add(key);
            result.CountImported();
            imported++;
        }

        if (imported > 0) await NotifyImportAsync(directory);
        return result;
    }

    private static string HistoryKey(string employeeId, string leaveTypeId, DateTime start, DateTime end) =>
        string.Join('|', employeeId, leaveTypeId, start.ToString("yyyy-MM-dd"), end.ToString("yyyy-MM-dd"));

    // One nudge for the whole import rather than one per row — same reasoning as
    // ClaimsService.NotifyImportAsync.
    private async Task NotifyImportAsync(EmployeeDirectorySnapshot directory)
    {
        var organizationId = _currentUser.OrganizationId;
        if (string.IsNullOrEmpty(organizationId)) return;

        await _realtime.PublishAsync(
            organizationId,
            directory.Members.Select(m => (string?)m.Id),
            RealtimeEventDto.For(RealtimeScope.LEAVE, RealtimeAction.UPDATED));
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
                AttachmentName = a.AttachmentName ?? a.XeroFileId,
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

    public async Task<LeaveExportResult> ExportBulkSummaryZipAsync(
        int year, IReadOnlyList<string>? employeeIds)
    {
        var members = await _memberships.GetForCurrentOrgAsync();
        var wanted = employeeIds is { Count: > 0 } ? employeeIds.ToHashSet() : null;
        var targets = members
            .Select(m => m.UserId)
            .Where(id => wanted is null || wanted.Contains(id))
            .Distinct()
            .ToList();

        if (targets.Count == 0)
            return new LeaveExportResult(false, true, [], "");

        var emails = await _supervision.GetEmailsAsync(targets);

        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var employeeId in targets)
            {
                var report = await GetSummaryReportAsync(employeeId, year);
                if (!report.Found || !report.Allowed) continue;   // skip, don't fail the batch

                var label = emails.GetValueOrDefault(employeeId) ?? employeeId;
                var name = UniqueName(used, $"{SafeFileName(label)}_{year}.pdf");

                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                await using var stream = entry.Open();
                var pdf = LeaveSummaryPdf.Render(report.Report!);
                await stream.WriteAsync(pdf);
            }
        }

        return new LeaveExportResult(true, true, buffer.ToArray(), $"leave-summaries-{year}.zip");
    }

    // Two people sharing a name would otherwise overwrite each other inside
    // the archive, so a clash becomes "Name_2026_2.pdf".
    private static string UniqueName(HashSet<string> used, string baseName)
    {
        if (used.Add(baseName)) return baseName;

        var stem = Path.GetFileNameWithoutExtension(baseName);
        var ext = Path.GetExtension(baseName);
        for (var n = 2; ; n++)
        {
            var candidate = $"{stem}_{n}{ext}";
            if (used.Add(candidate)) return candidate;
        }
    }

    private static string SafeFileName(string value)
    {
        var cleaned = new string(value
            .Select(c => Path.GetInvalidFileNameChars().Contains(c) || c is '/' or '\\' ? '_' : c)
            .ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "Employee" : cleaned;
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

    // Re-derive PRO_RATED accrual after the join date changes. Ported from
    // production's recomputeProRatedAccrualForEmployee, including its four
    // guards — this deliberately touches only rows it is SAFE to rewrite:
    //
    //   1. leave already taken  → skip. Never move a balance someone has spent
    //                             against; that could push them negative.
    //   2. per-employee method override → skip. An admin set it by hand.
    //   3. entitled days differ from the resolved default → skip. Someone has
    //      customised this row; re-deriving would silently undo them.
    //   4. not effectively PRO_RATED → skip. Nothing to pro-rate.
    public async Task<int> RecomputeProRatedAccrualAsync(string employeeId, int year)
    {
        var membership = await _memberships.GetForUserInCurrentOrgAsync(employeeId);
        if (membership is null) return 0;

        var typesById = (await _types.GetAllAsync()).ToDictionary(t => t.Id);
        var overrides = await _policies.GetLeaveEntitlementsAsync(employeeId);
        var rows = await _entitlements.GetForEmployeeYearAsync(employeeId, year);

        var taken = (await _apps.GetByEmployeeAsync(employeeId))
            .Where(a => a.Status == LeaveStatus.APPROVED && a.StartDate.Year == year)
            .GroupBy(a => a.LeaveTypeId)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.TotalDays));

        var touched = 0;
        foreach (var row in rows)
        {
            if (taken.GetValueOrDefault(row.LeaveTypeId) > 0) continue;          // 1
            if (row.AccrualMethod is not null) continue;                          // 2
            if (!typesById.TryGetValue(row.LeaveTypeId, out var type)) continue;

            var resolvedDays = overrides.GetValueOrDefault(type.Id, type.DefaultDays);
            if (Math.Abs(row.EntitledDays - resolvedDays) > Tolerance) continue;  // 3
            if (type.AccrualMethod != LeaveAccrualMethod.PRO_RATED) continue;     // 4

            var next = LeaveAccrualMath.ProRatedAccrualOnDate(
                row.EntitledDays, membership.JoinDate, year, DateTime.UtcNow);

            if (Math.Abs(next - row.AccruedDays) < 0.005) continue;   // nothing moved

            row.AccruedDays = next;
            row.UpdatedAt = DateTime.UtcNow;
            touched++;
        }

        if (touched > 0) await _entitlements.SaveAsync();
        return touched;
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

        if (dto.Duration != LeaveDuration.FULL_DAY && start != end)
            return new LeaveApplyResult(false, null,
                "Half-day leave must start and end on the same day");

        var (workingDays, holidays) = await ResolveCalendarAsync(start.Year);
        var totalDays = LeaveAccrualMath.ComputeTotalDays(
            start, end, dto.Duration, workingDays, holidays);
        if (totalDays <= 0)
            return new LeaveApplyResult(false, null, "Selected dates contain no working days");

        var year = start.Year;
        var entitlement = await EnsureEntitlementAsync(employeeId, type, year);

        if (type.Paid)
        {
            var available = await AvailableForApplyAsync(
                employeeId, type, entitlement, year, startDate: start);
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
            Duration = dto.Duration,
            TotalDays = totalDays,
            Reason = dto.Reason,
            XeroFileId = dto.XeroFileId,
            AttachmentName = dto.AttachmentName,
            Status = LeaveStatus.PENDING,
            CurrentStep = 0,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await _apps.AddAsync(application);
        await NotifyAsync(application, RealtimeAction.SUBMITTED, notifyApplicant: false);
        return new LeaveApplyResult(true, ToDto(application), null);
    }

    // An admin files leave on an employee's behalf. Production lands it
    // APPROVED, bypassing the supervisor chain, because the admin already has
    // authority to grant — and stamps AppliedByAdminId plus a synthetic
    // ADMIN_APPLIED trail entry so the history shows who really created it.
    //
    // The same balance check runs first, so an admin can't quietly over-grant
    // paid leave. Unpaid types stay exempt, as everywhere else.
    public async Task<LeaveApplyResult> ApplyOnBehalfAsync(
        string employeeId, CreateLeaveApplicationDto dto, string adminUserId)
    {
        var membership = await _memberships.GetForUserInCurrentOrgAsync(employeeId);
        if (membership is null) return new LeaveApplyResult(false, null, null);   // → 404

        var result = await ApplyAsync(dto, employeeId);
        if (!result.Ok) return result;

        var app = await _apps.GetByIdAsync(result.Application!.Id);
        app!.Status = LeaveStatus.APPROVED;
        app.DecidedAt = DateTime.UtcNow;
        app.AppliedByAdminId = adminUserId;
        AppendTrail(app, app.CurrentStep, adminUserId, "ADMIN_APPLIED", dto.Reason);
        app.UpdatedAt = DateTime.UtcNow;
        await _apps.UpdateAsync(app);

        // The employee never asked for this, so their calendar/balance changing
        // out from under them is exactly the case live updates exist for.
        await NotifyAsync(app, RealtimeAction.APPROVED, notifyApplicant: true);
        return new LeaveApplyResult(true, ToDto(app), null);
    }

    // The decision trail. Visible to whoever may read the employee's balances,
    // so an employee sees their own history and a supervisor sees their team's.
    public async Task<LeaveAuditResult> GetAuditTrailAsync(string applicationId)
    {
        var app = await _apps.GetByIdAsync(applicationId);
        if (app is null) return new LeaveAuditResult(false, false, null);
        if (!await CanReadBalancesAsync(app.EmployeeId))
            return new LeaveAuditResult(true, false, null);

        return new LeaveAuditResult(true, true, ReadTrail(app.Approvals));
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

        if (dto.Duration != LeaveDuration.FULL_DAY && start != end)
            return new LeaveApplyResult(false, null,
                "Half-day leave must start and end on the same day");

        var (workingDays, holidays) = await ResolveCalendarAsync(start.Year);
        var totalDays = LeaveAccrualMath.ComputeTotalDays(
            start, end, dto.Duration, workingDays, holidays);
        if (totalDays <= 0)
            return new LeaveApplyResult(false, null, "Selected dates contain no working days");

        var year = start.Year;
        var entitlement = await EnsureEntitlementAsync(actorUserId, type, year);

        if (type.Paid)
        {
            // Exclude THIS request from the pending total, or an edit would be
            // checked against a balance its own days are already reserving.
            var available = await AvailableForApplyAsync(
                actorUserId, type, entitlement, year,
                excludeApplicationId: id, startDate: start);
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
        app.Duration = dto.Duration;
        app.TotalDays = totalDays;
        app.Reason = dto.Reason;
        app.UpdatedAt = DateTime.UtcNow;
        await _apps.UpdateAsync(app);

        // The dates or type just changed under whoever is about to review it.
        await NotifyAsync(app, RealtimeAction.UPDATED, notifyApplicant: false);
        return new LeaveApplyResult(true, ToDto(app), null);
    }

    // The org's working week and public holidays for `year`. Both are org-wide:
    // production notes leave doesn't need attendance's project-scoped
    // resolution. Null WorkingDays means Mon-Fri.
    private async Task<(HashSet<int> WorkingDays, IReadOnlySet<DateTime> Holidays)>
        ResolveCalendarAsync(int year)
    {
        var org = _currentUser.OrganizationId is { } orgId
            ? await _organizations.GetByIdAsync(orgId)
            : null;

        // Main's Holidays module owns the calendar. GetInRangeAsync returns both
        // org-wide rows (ProjectId null) and project-scoped ones; leave is
        // org-wide, so only the former apply.
        var rows = await _holidays.GetInRangeAsync(
            new DateTime(year, 1, 1), new DateTime(year, 12, 31));

        return (LeaveAccrualMath.ParseWorkingDays(org?.WorkingDays),
                rows.Where(h => h.ProjectId is null)
                    .Select(h => DateTime.Parse(h.Date).Date)   // DTO carries yyyy-MM-dd
                    .ToHashSet());
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
    private static readonly JsonSerializerOptions ApprovalJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static List<LeaveApprovalEntryDto> ReadTrail(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<LeaveApprovalEntryDto>>(json, ApprovalJson) ?? []; }
        catch (JsonException) { return []; }   // never let a bad trail block a decision
    }

    private static void AppendTrail(
        LeaveApplication app, int step, string actorId, string decision, string? notes)
    {
        var trail = ReadTrail(app.Approvals);
        trail.Add(new LeaveApprovalEntryDto
        {
            Step = step,
            ApproverId = actorId,
            Decision = decision,
            DecidedAt = DateTime.UtcNow,
            Notes = notes,
        });
        app.Approvals = JsonSerializer.Serialize(trail, ApprovalJson);
    }

    private const double Tolerance = 0.0001;

    // Days the employee may book right now.
    //
    // Both APPROVED and PENDING days are subtracted. This is a DELIBERATE
    // divergence from production, which checks approved usage only: there, four
    // requests of 1 + 7 + 6 + 1 days each pass individually against a 14-day
    // entitlement, leaving 15 days pending that nothing ever summed. Approving
    // them in turn walks the employee past zero, because production's approve
    // path has no balance check either.
    //
    // Here a pending request holds its place, so the total can never exceed the
    // entitlement and an approver can say yes to anything in their queue without
    // checking the arithmetic themselves.
    //
    // The cost is that an employee can't submit alternatives ("either this week
    // or that week") and let the approver pick — they must cancel one first.
    // That is the accepted trade.
    private async Task<double> AvailableForApplyAsync(
        string employeeId, LeaveType type, LeaveEntitlement row, int year,
        string? excludeApplicationId = null, DateTime? startDate = null)
    {
        var apps = (await _apps.GetByEmployeeAsync(employeeId))
            .Where(a => a.LeaveTypeId == type.Id && a.StartDate.Year == year)
            // An EDIT must not be checked against days its own request is
            // already reserving, or raising 2 days to 3 would fail at 14/14.
            .Where(a => excludeApplicationId is null || a.Id != excludeApplicationId)
            .ToList();

        var taken = apps.Where(a => a.Status == LeaveStatus.APPROVED).Sum(a => a.TotalDays);
        var pending = apps.Where(a => a.Status == LeaveStatus.PENDING).Sum(a => a.TotalDays);

        var method = row.AccrualMethod ?? type.AccrualMethod;

        // For PRO_RATED, production checks what the employee will have accrued
        // BY THE LEAVE START DATE, not today — so booking December leave in
        // March is allowed if they'll have earned it by then. Needs a join date;
        // without one we fall back to the accrued-so-far figure.
        var accrued = row.AccruedDays;
        if (method == LeaveAccrualMethod.PRO_RATED && startDate is { } start)
        {
            var membership = await _memberships.GetForUserInCurrentOrgAsync(employeeId);
            var forecast = LeaveAccrualMath.ProRatedAccrualOnDate(
                row.EntitledDays, membership?.JoinDate, year, start);
            accrued = Math.Max(accrued, forecast);
        }

        var available = LeaveAccrualMath.AvailableDays(
            method, row.EntitledDays, accrued, row.CarriedDays, row.CarriedExpired, taken);

        return Math.Max(0, available - pending);
    }

    public async Task<LeaveTransitionResult> ApproveAsync(string id, string approverId)
    {
        var (app, error) = await AuthorizeAsync(id, approverId);
        if (error is not null) return error;

        var now = DateTime.UtcNow;
        AppendTrail(app!, app.CurrentStep, approverId, "APPROVED", null);
        var stepCount = await _router.StepCountAsync(Module, app.EmployeeId);
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

        // Still PENDING here means the chain advanced, so NotifyAsync also
        // reaches the next step's approver.
        await NotifyAsync(app, RealtimeAction.APPROVED, notifyApplicant: true);
        return new LeaveTransitionResult(true, true, ToDto(app));
    }

    public async Task<LeaveTransitionResult> RejectAsync(string id, string approverId, string? reviewNotes)
    {
        var (app, error) = await AuthorizeAsync(id, approverId);
        if (error is not null) return error;

        var now = DateTime.UtcNow;
        AppendTrail(app!, app.CurrentStep, approverId, "REJECTED", reviewNotes);
        app.Status = LeaveStatus.REJECTED;
        app.ReviewNotes = reviewNotes;
        app.DecidedAt = now;
        app.UpdatedAt = now;
        await _apps.UpdateAsync(app);

        // notifyApprovers: the request just left every reviewer's queue, and a
        // rejected row is no longer PENDING for NotifyAsync to infer that from.
        await NotifyAsync(app, RealtimeAction.REJECTED, notifyApplicant: true, notifyApprovers: true);
        return new LeaveTransitionResult(true, true, ToDto(app));
    }

    // Cancel is PENDING-only, and that is a deliberate narrowing.
    //
    // Production ships no cancel at all: `cancelLeaveApplication` exists in its
    // service layer but nothing calls it — an employee's only escape hatch is
    // editing a request that hasn't been reviewed yet. So there is no rule here
    // to port, and anything we allow is a NEW feature.
    //
    // Pending-only is the conservative shape: an employee can withdraw a request
    // nobody has acted on, which changes no balance (pending days were never
    // deducted). Undoing APPROVED leave means returning days someone already
    // granted — and, with no date guard, days the employee may already have
    // taken. That stays with an admin until the business decides otherwise.
    public async Task<LeaveTransitionResult> CancelAsync(string id, string userId)
    {
        var application = await _apps.GetByIdAsync(id);
        if (application is null)
            return new LeaveTransitionResult(false, false, null, "Application not found");

        if (application.EmployeeId != userId)
            return new LeaveTransitionResult(true, false, null, "Only the applicant can cancel");

        if (application.Status == LeaveStatus.CANCELLED)
            return new LeaveTransitionResult(true, true, ToDto(application));   // idempotent

        if (application.Status != LeaveStatus.PENDING)
            return new LeaveTransitionResult(true, false, ToDto(application),
                "Only pending leave can be cancelled");

        application.Status = LeaveStatus.CANCELLED;
        application.UpdatedAt = DateTime.UtcNow;
        await _apps.UpdateAsync(application);

        // The approvers are the ones who need this: a withdrawn request should
        // disappear from their queue rather than sit there until they reload.
        await NotifyAsync(application, RealtimeAction.CANCELLED,
            notifyApplicant: false, notifyApprovers: true);
        return new LeaveTransitionResult(true, true, ToDto(application));
    }

    // Live nudge for one leave request. Approvers are resolved from the row's
    // CURRENT step, so a multi-step chain notifies exactly the people who now
    // have to act — and nobody who doesn't.
    //
    // `notifyApprovers` forces the approver fan-out for the terminal states
    // (rejected, cancelled), where the PENDING check would otherwise conclude
    // there is nobody left to tell.
    private async Task NotifyAsync(
        LeaveApplication app,
        RealtimeAction action,
        bool notifyApplicant,
        bool notifyApprovers = false)
    {
        var targets = new List<string?>();
        if (notifyApplicant) targets.Add(app.EmployeeId);

        if (notifyApprovers || app.Status == LeaveStatus.PENDING)
            targets.AddRange(await _router.CurrentApproversAsync(Module, app.EmployeeId, app.CurrentStep));

        await _realtime.PublishAsync(
            app.OrganizationId,
            targets,
            RealtimeEventDto.For(RealtimeScope.LEAVE, action, app.Id));
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

        // DECIDED: 403 with a message, not a 404 that hides the request.
        // An earlier V2 draft returned 404 so a non-approver couldn't tell the
        // request existed. We match production instead — and it keeps the module
        // self-consistent, since /leave/balances/{employeeId} already answers 403
        // for someone in your org who isn't yours to read. Revisit only if
        // approvals are ever exposed to an external API-key caller.
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
        Duration = a.Duration,
        Reason = a.Reason,
        Status = a.Status,
        ReviewNotes = a.ReviewNotes,
        DecidedAt = Iso(a.DecidedAt),
        CreatedAt = Iso(a.CreatedAt) ?? string.Empty,
    };
}
