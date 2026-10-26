using System.Text.Json;
using System.Text.Json.Serialization;
using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Attendance;
using AltomateHR.Api.Modules.Attendance.Dtos;
using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Leave;
using AltomateHR.Api.Modules.Leave.Dtos;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Payroll.Dtos;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Modules.Policies;
using AltomateHR.Api.Modules.Policies.Entities;   // SalaryType, EmployeePolicy

namespace AltomateHR.Api.Modules.Payroll;

// Orchestrates a month of payroll: create the run, build every payslip through
// PayslipCalculator, snapshot the result.
//
// The one rule that shapes everything here: GENERATION IS DESTRUCTIVE. Pressing
// Generate discards the run's payslips and line items and rebuilds them from the
// employees' current profiles. That is what makes a draft run safe to re-run
// after fixing a profile — and why anything that must survive a regeneration
// cannot live on a payslip alone.
public class PayrollRunService : IPayrollRunService
{
    private readonly IPayrollRunRepository _runs;
    private readonly IPayslipRepository _payslips;
    private readonly IPayrollSettingsService _settings;
    private readonly IDirectoryService _directory;
    private readonly IPayrollRunAdjustmentRepository _adjustments;
    private readonly IPayrollRunClaimRepository _runClaims;
    private readonly IPayrollRunMemberRepository _runMembers;
    private readonly IPolicyService _policies;
    private readonly IStatutoryFileService _statutory;
    private readonly IHoursSummaryService _hours;
    private readonly ILeaveService _leave;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;
    private readonly IPayrollXeroSyncService _xeroSync;
    private readonly IEmployeeLoanService _loans;
    private readonly Overtime.IApprovedOvertimeService _approvedOvertime;
    // Optional so hand-built instances in tests need not supply it. A leaver
    // transferred to another company is paid their final month here after the
    // transfer ran; submitting (or reverting) that month re-carries their
    // year-to-date to the new company.
    private readonly Employees.IEmployeeTransferService? _transfers;
    // A policy-limited admin sees only their people's payslips; the run's
    // stored totals are company-wide, so for them they're re-summed from what
    // they can see.
    private readonly IEmployeeScope? _scope;
    // Optional for the same reason. Without it, unpaid leave decided after
    // its month was filed is not settled on a later run.
    private readonly IUnpaidLeaveSettlementRepository? _settlements;
    private readonly IPayrollDraftStaleness? _drafts;

    public PayrollRunService(
        IPayrollRunRepository runs,
        IPayslipRepository payslips,
        IPayrollSettingsService settings,
        IDirectoryService directory,
        IPayrollRunAdjustmentRepository adjustments,
        IPayrollRunClaimRepository runClaims,
        IPayrollRunMemberRepository runMembers,
        IPolicyService policies,
        IStatutoryFileService statutory,
        IHoursSummaryService hours,
        ILeaveService leave,
        ICurrentUser currentUser,
        IAuditService audit,
        IPayrollXeroSyncService xeroSync,
        IEmployeeLoanService loans,
        Overtime.IApprovedOvertimeService approvedOvertime,
        Employees.IEmployeeTransferService? transfers = null,
        IEmployeeScope? scope = null,
        IUnpaidLeaveSettlementRepository? settlements = null,
        IPayrollDraftStaleness? drafts = null)
    {
        _scope = scope;
        _settlements = settlements;
        _drafts = drafts;
        _transfers = transfers;
        _approvedOvertime = approvedOvertime;
        _statutory = statutory;
        _hours = hours;
        _leave = leave;
        _currentUser = currentUser;
        _runs = runs;
        _payslips = payslips;
        _settings = settings;
        _directory = directory;
        _adjustments = adjustments;
        _runClaims = runClaims;
        _runMembers = runMembers;
        _policies = policies;
        _audit = audit;
        _xeroSync = xeroSync;
        _loans = loans;
    }

    // The JSON on EmployeeProfile was written by the reference app, so reads are
    // case-insensitive and enums arrive as names rather than ordinals.
    private static readonly JsonSerializerOptions ProfileJson = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    // Shared with every reader of these columns — see PayrollSnapshotJson.
    private static JsonSerializerOptions SnapshotJson => PayrollSnapshotJson.Options;

    public async Task<List<PayrollRunDto>> GetAllAsync()
    {
        var runs = await _runs.GetAllAsync();
        if (_scope is not { IsLimited: true }) return runs.Select(ToDto).ToList();

        var result = new List<PayrollRunDto>(runs.Count);
        foreach (var run in runs)
            result.Add(WithTotalsOf(run, await _payslips.GetForRunAsync(run.Id)));   // filtered to scope
        return result;
    }

    // The run as a limited admin may see it: the stored totals replaced by the
    // sums of the payslips in their scope.
    private static PayrollRunDto WithTotalsOf(PayrollRun run, IReadOnlyList<Payslip> visible)
    {
        var dto = ToDto(run);
        var t = new PayrollRun();
        ApplyTotals(t, visible.ToList());
        dto.EmployeeCount = t.EmployeeCount;
        dto.TotalGross = t.TotalGross;
        dto.TotalGrossShown = PayrollGrossShown.Of(t);
        dto.TotalNet = t.TotalNet;
        dto.TotalEmployeeEpf = t.TotalEmployeeEpf;
        dto.TotalEmployerEpf = t.TotalEmployerEpf;
        dto.TotalEmployeeSocso = t.TotalEmployeeSocso;
        dto.TotalEmployerSocso = t.TotalEmployerSocso;
        dto.TotalEmployeeEis = t.TotalEmployeeEis;
        dto.TotalEmployerEis = t.TotalEmployerEis;
        dto.TotalEmployeeSkbbk = t.TotalEmployeeSkbbk;
        dto.TotalPcb = t.TotalPcb;
        dto.TotalCp38 = t.TotalCp38;
        dto.TotalZakat = t.TotalZakat;
        dto.TotalHrdf = t.TotalHrdf;
        dto.EmployeesSubjectToHrdf = t.EmployeesSubjectToHrdf;
        dto.TotalWagesSubjectToHrdf = t.TotalWagesSubjectToHrdf;
        dto.TotalCostToEmployer = t.TotalCostToEmployer;
        return dto;
    }

    public async Task<PayrollRunDetailDto?> GetAsync(string id)
    {
        var run = await _runs.GetByIdAsync(id);
        if (run is null) return null;

        var payslips = await _payslips.GetForRunAsync(run.Id);
        var lineItems = await _payslips.GetLineItemsForRunAsync(run.Id);
        var memberIds = (await _runMembers.GetForRunAsync(run.Id))
            .Select(m => m.EmployeeProfileId)
            .ToList();

        var detail = BuildDetail(run, payslips, lineItems, memberIds);
        if (_scope is { IsLimited: true }) detail.Run = WithTotalsOf(run, payslips);
        return detail;
    }

    public async Task<PayrollRunSaveResult> CreateAsync(CreatePayrollRunDto dto)
    {
        var month = PeriodLabel(dto.PeriodYear, dto.PeriodMonth);
        var name = Blank(dto.Name);
        var monthRuns = await _runs.GetForPeriodAsync(dto.PeriodYear, dto.PeriodMonth);

        // A month can be split into runs for different staff groups. At most
        // one of them goes unnamed — the rest need a name, so the runs and
        // their files can be told apart. A caller that doesn't send one
        // (ABPay's posting) gets the refusal it always got while the month
        // has an unnamed run, and can still recreate its own after deleting it.
        if (name is null && monthRuns.Any(r => Blank(r.Name) is null))
        {
            return Refused(
                $"A payroll run already exists for {month}. To add another run for the month, "
                + "give it a name — for example the staff group it pays.");
        }

        var run = new PayrollRun
        {
            PeriodYear = dto.PeriodYear,
            PeriodMonth = dto.PeriodMonth,
            Name = name,
            RunNumber = monthRuns.Count == 0 ? 1 : monthRuns.Max(r => r.RunNumber) + 1,
            Status = PayrollRunStatus.DRAFT,
            Source = PayrollRunSource.COMPUTED,
            SkipProration = dto.SkipProration,
        };
        if (NameClash(monthRuns, run) is { } clash) return Refused(clash);

        // The admin's chosen roster (policies − unticked employees), less
        // anyone already in another run this month — a person is paid in one
        // run a month. The picker greys them out; this is the guarantee.
        var members = await ResolveScopeAsync(
            dto.PolicyIds, dto.ExcludedEmployeeProfileIds, dto.PeriodYear, dto.PeriodMonth);
        var takenAtCreation = new List<string>();
        if (monthRuns.Count > 0)
        {
            var taken = await TakenInMonthAsync(dto.PeriodYear, dto.PeriodMonth, exceptRunId: null);
            takenAtCreation = members.Where(taken.ContainsKey).ToList();
            members = members.Where(id => !taken.ContainsKey(id)).ToList();
            if (members.Count == 0)
            {
                return Refused(
                    $"Everyone you picked is already in another {month} run. A person can be in "
                    + "only one run a month.");
            }

            // From here on the month has several runs, so "a run with no
            // roster is everyone" can't hold for the existing ones: write
            // down who each of them is for.
            foreach (var other in monthRuns) await PinRosterAsync(other);
        }

        // Remember the choice itself — but only for a draft made with the
        // policy picker (which always sends policies), so a re-run can add
        // people who become payable on those policies later (see
        // GenerateAsync). A run made from an explicit list — ABPay's, which
        // sends only exclusions — never grows: its roster is the timesheet.
        // Decided here, from how the run was made, not from a setting that can
        // be changed afterwards. Whoever was in another run at creation counts
        // as unticked: if that run is deleted they don't drift into this one.
        if (members.Count > 0 && dto.PolicyIds is { Count: > 0 })
        {
            run.ScopePolicyIdsJson = JsonSerializer.Serialize(dto.PolicyIds);
            run.ScopeExcludedProfileIdsJson = JsonSerializer.Serialize(
                (dto.ExcludedEmployeeProfileIds ?? []).Concat(takenAtCreation).Distinct().ToList());
        }

        // The run and its roster in one save. Generation reads the roster
        // back; a run with no rows (only ever the month's sole run) means
        // "everyone".
        if (!await _runs.TryAddWithRosterAsync(run, members))
        {
            return Refused(
                $"Another {month} payroll run was started at the same moment. Reload and try again.");
        }

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.PayrollRunCreate,
            $"Started the {RunLabel(run)} payroll run",
            TargetType: "PayrollRun",
            TargetId: run.Id,
            Metadata: new { run.PeriodYear, run.PeriodMonth, run.Name, MemberCount = members.Count, run.SkipProration }));

        return new PayrollRunSaveResult(true, ToDto(run), null);
    }

    // Why `run` (as named) can't sit beside the month's other runs, or null.
    // Two runs may not share a name, nor names that make the same file name
    // ("Team 1" and "Team-1" both file as "Team1").
    private static string? NameClash(IEnumerable<PayrollRun> monthRuns, PayrollRun run)
    {
        var name = Blank(run.Name);
        if (name is null) return null;
        var tag = PayrollPeriodLabel.FileTag(run);
        var month = PeriodLabel(run.PeriodYear, run.PeriodMonth);
        foreach (var other in monthRuns.Where(r => r.Id != run.Id))
        {
            if (string.Equals(Blank(other.Name), name, StringComparison.OrdinalIgnoreCase))
                return $"{month} already has a run named \"{name}\". Choose another name.";
            if (tag.Length > 0
                && string.Equals(PayrollPeriodLabel.FileTag(other), tag, StringComparison.OrdinalIgnoreCase))
            {
                return $"\"{name}\" is too close to {RunLabel(other)} — their files would have the "
                       + "same name. Choose a more different name.";
            }
        }
        return null;
    }

    // Who each of the month's runs (other than `exceptRunId`) is for, by
    // employee profile id → that run. A run's people are its payslips once
    // run, otherwise its roster; a sole run with neither is for everyone
    // payable that month.
    private async Task<Dictionary<string, PayrollRun>> TakenInMonthAsync(
        int year, int month, string? exceptRunId)
    {
        var taken = new Dictionary<string, PayrollRun>(StringComparer.Ordinal);
        foreach (var other in await _runs.GetForPeriodAsync(year, month))
        {
            if (other.Id == exceptRunId) continue;
            var people = await PeopleOfAsync(other)
                         ?? [.. await ResolveScopeAsync(null, null, year, month)];
            foreach (var id in people) taken.TryAdd(id, other);
        }
        return taken;
    }

    // Writes down a run's people as its roster when it has none ("everyone").
    // Done when a second run joins the month, so the first stops claiming
    // whoever becomes payable later — and so a newcomer can go to either.
    private async Task PinRosterAsync(PayrollRun run)
    {
        if ((await _runMembers.GetForRunAsync(run.Id)).Count > 0) return;
        var people = await PeopleOfAsync(run)
                     ?? [.. await ResolveScopeAsync(null, null, run.PeriodYear, run.PeriodMonth)];
        if (people.Count > 0) await _runMembers.ReplaceForRunAsync(run, people);
    }

    public async Task<PayrollRunSaveResult> UpdateAsync(string id, UpdatePayrollRunDto dto)
    {
        var run = await _runs.GetByIdAsync(id);
        if (run is null) return new PayrollRunSaveResult(false, null, null);

        // All or nothing: skipProration on a run that isn't a draft is refused
        // (as it always was) before anything else in the request is saved.
        if (dto.SkipProration is not null && run.Status != PayrollRunStatus.DRAFT)
        {
            return new PayrollRunSaveResult(
                false, null, "Only a draft payroll run can be changed.");
        }

        // A rename is only a label, so it's allowed in any status.
        if (dto.Name is not null)
        {
            var renamed = await RenameAsync(run, Blank(dto.Name));
            if (!renamed.Ok || dto.SkipProration is null) return renamed;
        }

        // Changing how pay is computed on a filed or pending month would leave
        // its payslips describing a rule the run no longer says it used.
        if (run.Status != PayrollRunStatus.DRAFT)
        {
            return new PayrollRunSaveResult(
                false, null, "Only a draft payroll run can be changed.");
        }

        if (dto.SkipProration is not { } skip || skip == run.SkipProration)
            return new PayrollRunSaveResult(true, ToDto(run), null);

        run.SkipProration = skip;

        // The payslips on screen were priced under the old setting, so the run
        // asks for a re-run — the same stamp an adjustment edit leaves.
        if (run.GeneratedAt is not null) run.LastMutatedAt = DateTime.UtcNow;
        await _runs.UpdateAsync(run);

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.PayrollRunUpdate,
            skip
                ? $"Set the {RunLabel(run)} payroll run to use pay figures as final (no proration)"
                : $"Set the {RunLabel(run)} payroll run to prorate joiners and leavers",
            TargetType: "PayrollRun",
            TargetId: run.Id,
            Metadata: new { run.PeriodYear, run.PeriodMonth, SkipProration = skip }));

        return new PayrollRunSaveResult(true, ToDto(run), null);
    }

    private async Task<PayrollRunSaveResult> RenameAsync(PayrollRun run, string? name)
    {
        if (string.Equals(Blank(run.Name), name, StringComparison.Ordinal))
            return new PayrollRunSaveResult(true, ToDto(run), null);

        var monthRuns = await _runs.GetForPeriodAsync(run.PeriodYear, run.PeriodMonth);
        var month = RunLabel(run);
        if (name is null && monthRuns.Any(r => r.Id != run.Id && Blank(r.Name) is null))
            return Refused($"Another {month} run has no name, so this one needs one.");
        if (NameClash(monthRuns, new PayrollRun
            {
                Id = run.Id, PeriodYear = run.PeriodYear, PeriodMonth = run.PeriodMonth,
                Name = name, RunNumber = run.RunNumber,
            }) is { } clash)
            return Refused(clash);

        var before = RunLabel(run);
        run.Name = name;
        await _runs.UpdateAsync(run);

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.PayrollRunUpdate,
            $"Renamed the {before} payroll run to {RunLabel(run)}",
            TargetType: "PayrollRun",
            TargetId: run.Id,
            Metadata: new { run.PeriodYear, run.PeriodMonth, run.Name }));

        return new PayrollRunSaveResult(true, ToDto(run), null);
    }

    // The "Start a payroll run" picker: every non-archived policy the admin can
    // scope a run to, each carrying the payable (complete, non-archived)
    // employees under it. Employees with no effective policy are omitted — a run
    // is scoped through a policy, so they cannot be selected.
    public async Task<PayrollRunPickerDto> GetPickerAsync(
        int? year = null, int? month = null, string? exceptRunId = null)
    {
        // Who is already in another run this month: shown, but not pickable.
        var taken = year is { } y && month is { } m
            ? await TakenInMonthAsync(y, m, exceptRunId)
            : new Dictionary<string, PayrollRun>(StringComparer.Ordinal);

        var eligible = (await _directory.PayrollProfilesAsync())
            .Where(p => PayableIn(p, year, month) && PayrollProfileReadiness.IsComplete(p))
            .ToList();

        var memberships = (await _directory.GetMembershipsForCurrentOrgAsync())
            .ToDictionary(m => m.UserId, StringComparer.Ordinal);
        var users = (await _directory.GetUsersAsync())
            .ToDictionary(u => u.Id, StringComparer.Ordinal);
        var policyByUser = await _policies.GetEffectivePoliciesForEmployeesAsync(
            eligible.Select(p => p.UserId));
        var policies = (await _policies.GetAllAsync()).Where(p => !p.IsArchived).ToList();

        var membersByPolicy = new Dictionary<string, List<PayrollPickerMemberDto>>(StringComparer.Ordinal);
        foreach (var profile in eligible)
        {
            if (!policyByUser.TryGetValue(profile.UserId, out var policy)) continue;

            memberships.TryGetValue(profile.UserId, out var membership);
            if (!membersByPolicy.TryGetValue(policy.Id, out var list))
                membersByPolicy[policy.Id] = list = [];

            list.Add(new PayrollPickerMemberDto
            {
                EmployeeProfileId = profile.Id,
                // A picker, so the nickname helps find them ("Ahmad (Mat)").
                Name = users.TryGetValue(profile.UserId, out var user)
                    ? PersonName.WithNickname(user.Name, membership?.Nickname)
                    : string.Empty,
                EmployeeId = membership?.EmployeeNumber ?? string.Empty,
                JobTitle = membership?.JobTitle ?? string.Empty,
                TakenByRunId = taken.GetValueOrDefault(profile.Id)?.Id,
                TakenByRunLabel = taken.TryGetValue(profile.Id, out var holder) ? RunLabel(holder) : null,
            });
        }

        var dto = new PayrollRunPickerDto();
        foreach (var policy in policies
            .OrderByDescending(p => p.IsDefault)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            dto.Policies.Add(new PayrollPickerPolicyDto
            {
                Id = policy.Id,
                Name = policy.Name,
                IsDefault = policy.IsDefault,
                Members = membersByPolicy.TryGetValue(policy.Id, out var list)
                    ? list.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList()
                    : [],
            });
        }

        return dto;
    }

    // Included = payable employees under the selected policies, minus the ones
    // the admin unticked. A null/empty policy list means "every policy".
    private async Task<List<string>> ResolveScopeAsync(
        List<string>? policyIds, List<string>? excludedProfileIds, int year, int month)
    {
        var eligible = (await _directory.PayrollProfilesAsync())
            .Where(p => PayableIn(p, year, month) && PayrollProfileReadiness.IsComplete(p))
            .ToList();

        var policyByUser = await _policies.GetEffectivePoliciesForEmployeesAsync(
            eligible.Select(p => p.UserId));

        var selected = policyIds is null || policyIds.Count == 0
            ? null
            : new HashSet<string>(policyIds, StringComparer.Ordinal);
        var excluded = new HashSet<string>(
            excludedProfileIds ?? [], StringComparer.Ordinal);

        var included = new List<string>();
        foreach (var profile in eligible)
        {
            if (excluded.Contains(profile.Id)) continue;

            if (selected is not null)
            {
                var policyId = policyByUser.TryGetValue(profile.UserId, out var policy)
                    ? policy.Id
                    : null;
                if (policyId is null || !selected.Contains(policyId)) continue;
            }

            included.Add(profile.Id);
        }

        return included;
    }

    public async Task<PayrollRunGenerateResult> GenerateAsync(string id)
    {
        var run = await _runs.GetByIdAsync(id);
        if (run is null) return new PayrollRunGenerateResult(false, null, null);

        // A submitted run is a filed figure. Rebuilding its payslips would
        // rewrite history that KWSP, PERKESO and LHDN have already been told.
        if (run.Status != PayrollRunStatus.DRAFT)
        {
            return new PayrollRunGenerateResult(
                false, null, "Payroll can only be run on a draft.");
        }

        // Nor an imported month, however it got here: running payroll would
        // replace the previous system's figures with today's calculation.
        if (run.Source == PayrollRunSource.IMPORTED)
        {
            return new PayrollRunGenerateResult(false, null, ImportedGenerateRefusal);
        }

        var settings = await _settings.GetEffectiveAsync();

        // Employees and Supervisors only. A draft created before this rule may
        // list an admin as a member; they are simply not paid by it.
        var profiles = await _directory.PayrollProfilesAsync();

        // Honour the roster the admin chose when the draft was created. A run
        // with no member rows predates the picker (or is an import), so it keeps
        // the old behaviour of pulling everyone; SkipReasonFor is still the
        // final gate either way.
        var memberIds = (await _runMembers.GetForRunAsync(run.Id))
            .Select(m => m.EmployeeProfileId)
            .ToHashSet(StringComparer.Ordinal);

        // Someone who became payable on the chosen policies after the draft
        // was made — transferred in, or newly hired — joins the roster on the
        // re-run. Without this a transfer marked the draft stale but "Re-run
        // payroll" still left the newcomer out. Only for drafts made with the
        // policy picker that have never been approved (see CreateAsync and
        // RevertOne), and never anyone the admin unticked.
        if (memberIds.Count > 0 && run.ScopePolicyIdsJson is not null)
        {
            var policyIds = JsonSerializer.Deserialize<List<string>>(run.ScopePolicyIdsJson) ?? [];
            var excluded = JsonSerializer.Deserialize<List<string>>(run.ScopeExcludedProfileIdsJson ?? "[]") ?? [];
            var inOtherRuns = await TakenInMonthAsync(run.PeriodYear, run.PeriodMonth, run.Id);
            var newcomers = (await ResolveScopeAsync(policyIds, excluded, run.PeriodYear, run.PeriodMonth))
                .Where(id => !memberIds.Contains(id) && !inOtherRuns.ContainsKey(id))
                .ToList();
            if (newcomers.Count > 0)
            {
                memberIds.UnionWith(newcomers);
                await _runMembers.ReplaceForRunAsync(run, memberIds);
            }
        }

        if (memberIds.Count > 0)
            profiles = profiles.Where(p => memberIds.Contains(p.Id)).ToList();

        // Never pay someone a second time in a month: anyone another of this
        // month's runs holds is left out here, whatever this run's roster says.
        var heldElsewhere = await TakenInMonthAsync(run.PeriodYear, run.PeriodMonth, run.Id);
        if (heldElsewhere.Count > 0)
            profiles = profiles.Where(p => !heldElsewhere.ContainsKey(p.Id)).ToList();

        var memberships = (await _directory.GetMembershipsForCurrentOrgAsync())
            .ToDictionary(m => m.UserId, StringComparer.Ordinal);
        var users = (await _directory.GetUsersAsync())
            .ToDictionary(u => u.Id, StringComparer.Ordinal);

        // This year's locked-in figures, excluding this run — a run must never
        // feed its own YTD baseline, or every regeneration would compound.
        var ytdByEmployee = await _payslips.GetYtdByEmployeeAsync(run.PeriodYear, run.Id);

        // The two tables that exist because THIS method is destructive. Both
        // are read up front, keyed by employee: what the admin typed, and the
        // claims they attached to be paid back through this month's pay.
        var adjustments = (await _adjustments.GetForRunAsync(run.Id))
            .ToDictionary(a => a.EmployeeProfileId, StringComparer.Ordinal);

        var claimsByEmployee = (await _runClaims.GetForRunAsync(run.Id))
            .GroupBy(c => c.EmployeeProfileId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        // OT multipliers come from each employee's policy. Resolved in one
        // batch — the per-employee call is two queries a head, which on a
        // 200-person run bought 400 round trips for a handful of policies.
        var policies = await _policies.GetEffectivePoliciesForEmployeesAsync(
            profiles.Select(p => p.UserId));

        // Attendance and leave for the period, both org-wide in one read.
        // `to` is the last day of the month — inclusive, which is what both
        // seams expect.
        var periodStart = new DateTime(run.PeriodYear, run.PeriodMonth, 1);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);

        var hoursByUser = await _hours.GetHoursForEmployeesAsync(
            profiles.Select(p => p.UserId), periodStart, periodEnd);

        // Unpaid leave decided after an earlier month of this year was filed —
        // approved late, cancelled or changed — settled on this run; and which
        // applications this month docks, recorded so a later run can tell.
        // Every approved unpaid part is recorded, a 0-day one too (a weekend
        // part): settled at 0, a later work-week change can't re-price it.
        var carriesByProfile = await UnpaidLeaveCarriesAsync(run, profiles);
        var ownPartsByUser = _settlements is null
            ? []
            : (await _leave.GetLeavePartsForOrgAsync(periodStart, periodEnd))
                .Where(p => p.IsApprovedUnpaid)
                .GroupBy(p => p.UserId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var settlements = new List<UnpaidLeaveSettlement>();

        // Keyed by user id, like the hours — the Leave module works in users.
        // With settlements, from the same read as what's recorded, so a leave
        // decided mid-generation can't be docked without a record or the
        // other way round.
        var unpaidDaysByUser = _settlements is null
            ? await _leave.GetApprovedUnpaidDaysForOrgAsync(periodStart, periodEnd)
            : ownPartsByUser.ToDictionary(kv => kv.Key, kv => kv.Value.Sum(p => p.Days), StringComparer.Ordinal);

        // Approved overtime requests for the month, split by day type. Paid by
        // default — see PayrollOvertimeHours for when the admin's figure wins.
        var approvedOtByUser = await _approvedOvertime.GetApprovedMinutesAsync(periodStart, periodEnd);

        // What each employee's active loans take this period. One query for
        // the whole run, like the hours and the leave above it.
        var loanRepayments = await _loans.GetRepaymentsForPeriodAsync(
            run.PeriodYear, run.PeriodMonth);

        var payslips = new List<Payslip>();
        var lineItems = new List<PayslipLineItem>();
        var skipped = new List<SkippedEmployeeDto>();
        var now = DateTime.UtcNow;

        foreach (var profile in profiles)
        {
            var name = users.TryGetValue(profile.UserId, out var user)
                ? user.Name
                : string.Empty;

            var skipReason = SkipReasonFor(profile, run);
            if (skipReason is not null)
            {
                skipped.Add(new SkippedEmployeeDto
                {
                    EmployeeProfileId = profile.Id,
                    Name = name,
                    Reason = skipReason,
                });
                continue;
            }

            var ytd = ytdByEmployee.TryGetValue(profile.Id, out var found)
                ? found
                : new PayrollYtdTotals();

            adjustments.TryGetValue(profile.Id, out var adjustment);
            policies.TryGetValue(profile.UserId, out var policy);
            var attached = claimsByEmployee.GetValueOrDefault(profile.Id) ?? [];
            var buckets = hoursByUser.GetValueOrDefault(profile.UserId);
            // Typed on the run (staff not on the Leave module yet) replaces what
            // the Leave module approved, so no day is docked twice.
            var unpaidDays = adjustment?.UnpaidLeaveDays
                ?? (decimal)unpaidDaysByUser.GetValueOrDefault(profile.UserId);

            var loanRepayment = loanRepayments.GetValueOrDefault(profile.Id);
            var overtime = PayrollOvertimeHours.Resolve(
                adjustment, approvedOtByUser.GetValueOrDefault(profile.UserId));

            var settledMonths = carriesByProfile.GetValueOrDefault(profile.Id) ?? [];
            var carries = settledMonths.SelectMany(r => r.Lines).ToList();
            var result = PayslipCalculator.Calculate(
                BuildInput(
                    profile, settings, run, ytd, adjustment, attached, policy,
                    buckets, unpaidDays, loanRepayment, overtime, carries));

            memberships.TryGetValue(profile.UserId, out var membership);

            var payslip = ToPayslip(run, profile, name, membership, result, now);
            payslip.TracksUnpaidLeave = _settlements is not null;
            payslips.Add(payslip);
            lineItems.AddRange(result.LineItems.Select((li, i) =>
                WithCarry(ToLineItem(payslip.Id, li, now, i + 1), carries)));

            // Typed days replaced the Leave module's on purpose, so this month
            // settles no application; an hourly person docks nothing.
            var ownParts = adjustment?.UnpaidLeaveDays is null && profile.SalaryType == SalaryType.MONTHLY
                ? ownPartsByUser.GetValueOrDefault(profile.UserId) ?? []
                : [];
            settlements.AddRange(ownParts.Select(p =>
                Settlement(run, payslip, run.PeriodYear, run.PeriodMonth, p.ApplicationId, (decimal)p.Days, p, null, now)));
            settlements.AddRange(settledMonths.SelectMany(r => r.Owed.Select(o =>
                Settlement(run, payslip, r.Year, r.Month, o.ApplicationId, o.Days, o.Part, o.Last, now))));
        }

        await _payslips.ReplaceForRunAsync(run.Id, payslips, lineItems);
        if (_settlements is not null) await _settlements.ReplaceForRunAsync(run.Id, settlements);

        ApplyTotals(run, payslips);
        run.GeneratedAt = now;
        // The payslips now match their inputs, so the staleness warning clears.
        run.LastMutatedAt = null;
        await _runs.UpdateAsync(run);

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.PayrollRunGenerate,
            $"Ran payroll — {payslips.Count} payslip(s) for the "
            + $"{RunLabel(run)} payroll run",
            TargetType: "PayrollRun",
            TargetId: run.Id,
            Metadata: new
            {
                run.PeriodYear,
                run.PeriodMonth,
                PayslipCount = payslips.Count,
                SkippedCount = skipped.Count,
                run.TotalGross,
                run.TotalNet,
                run.TotalCostToEmployer,
            }));

        return new PayrollRunGenerateResult(true, new GeneratePayrollRunResultDto
        {
            Detail = BuildDetail(run, payslips, lineItems, memberIds.ToList()),
            PayslipCount = payslips.Count,
            SkippedEmployees = skipped,
        }, null);
    }

    // ─── The status machine ─────────────────────────────────────────────

    public async Task<PayrollRunSaveResult> SubmitForApprovalAsync(string id)
    {
        var run = await _runs.GetByIdAsync(id);
        if (run is null) return NotFound();

        if (run.Status != PayrollRunStatus.DRAFT)
        {
            return Refused(run.Status == PayrollRunStatus.PENDING_APPROVAL
                ? "This run is already awaiting approval."
                : "This run has already been submitted.");
        }

        var payslips = await _payslips.GetForRunAsync(run.Id);

        // Guard 1 — an empty run cannot be finalised. Usually it just means
        // Generate was never pressed.
        if (payslips.Count == 0)
        {
            return Refused(
                "Run payroll before submitting — an empty run cannot be finalised.");
        }

        // Guard 2 — staleness. The inputs moved after the payslips were built,
        // so the figures about to be filed are not the ones the inputs now
        // imply. LastMutatedAt is set by every adjustment and claim mutation
        // and cleared by generation.
        var latestGeneration = payslips.Max(p => p.CreatedAt);
        if (run.LastMutatedAt is not null && run.LastMutatedAt > latestGeneration)
        {
            return Refused(
                "Something this run uses changed after payroll was run. "
                + "Re-run payroll before submitting.");
        }

        // Guard 3 — nobody may take home LESS than nothing. Deductions larger
        // than someone's pay is a data-entry mistake far more often than it is
        // a real month, and once submitted the payslip is immutable.
        //
        // Exactly RM 0 is allowed, as the previous system allowed it
        // (`netPay < 0`): a RM 0 base salary — a director or a placeholder
        // paid nothing this month — is a real case, and the bank files
        // already skip a zero-pay row rather than paying it.
        //
        // Note this is a SUBMIT-time guard, not a floor inside the calculator:
        // the calculator lets net go negative (as the reference's does), and
        // whether EA 1955 s.24 requires a floor is still an open question — see
        // MIGRATION.md. Refusing here is the protection that exists today.
        var negative = payslips.Where(p => p.NetPay < 0m).ToList();
        if (negative.Count > 0)
        {
            var names = string.Join(", ", negative.Take(5).Select(p => p.SnapshotName));
            var more = negative.Count > 5 ? $" and {negative.Count - 5} more" : string.Empty;

            return Refused(
                $"Cannot submit — net pay is negative for: {names}{more}. "
                + "Reduce their deductions, then re-run payroll.");
        }

        // Guard 4 — chronological order. YTD is read off SUBMITTED runs only,
        // and a payslip snapshot is immutable once filed. Submitting February
        // before January freezes February's PCB, EPF and SOCSO against a zero
        // YTD, and submitting January afterwards cannot go back and fix it.
        var refusal = await ChronologyRefusalAsync(run);
        if (refusal is not null) return Refused(refusal);

        // Guard 5 — statutory readiness. The submission files this month will
        // need are defined by the generators, so this asks THEM what is
        // missing. Failing here, while the run is still a draft and freely
        // editable, is far cheaper than failing at generation time: by then
        // the run is filed, and fixing it means a revert that cascades through
        // the rest of the year.
        var readiness = await _statutory.GetReadinessAsync(run.Id);
        if (readiness is not null && !readiness.Ok)
        {
            return Refused($"Cannot submit — fix these first: {readiness.Describe()}");
        }

        var now = DateTime.UtcNow;
        run.Status = PayrollRunStatus.PENDING_APPROVAL;
        run.SubmittedForApprovalAt = now;
        run.SubmittedForApprovalById = _currentUser.UserId;
        // A fresh submission clears the last rejection — the reason described
        // the version that was sent back, not this one.
        run.ApprovalRejectionReason = null;
        await _runs.UpdateAsync(run);

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.PayrollRunSubmitForApproval,
            $"Submitted the {RunLabel(run)} payroll run for approval",
            TargetType: "PayrollRun",
            TargetId: run.Id,
            Metadata: new
            {
                run.PeriodYear,
                run.PeriodMonth,
                PayslipCount = payslips.Count,
                run.TotalNet,
                run.TotalCostToEmployer,
            }));

        return new PayrollRunSaveResult(true, ToDto(run), null);
    }

    // Null means the order is fine. Two ways it is not, both fixed the same
    // way — submit the earlier month first.
    private async Task<string?> ChronologyRefusalAsync(PayrollRun run)
    {
        var (prevYear, prevMonth) = run.PeriodMonth > 1
            ? (run.PeriodYear, run.PeriodMonth - 1)
            : (run.PeriodYear - 1, 12);

        var previousRuns = await _runs.GetForPeriodAsync(prevYear, prevMonth);

        if (previousRuns.Count > 0)
        {
            // Per person: only a previous-month run that shares someone with
            // this one has to be submitted first. A month can hold several
            // runs for different groups, and one group's slow approval must
            // not hold up another's.
            var people = await PeopleOfAsync(run);
            foreach (var previous in previousRuns.Where(r => r.Status != PayrollRunStatus.SUBMITTED))
            {
                var theirs = await PeopleOfAsync(previous);
                if (people is null || theirs is null || people.Overlaps(theirs))
                {
                    return $"Submit {PeriodLabel(prevYear, prevMonth)} first — payroll runs are "
                           + "submitted in order, and that month's run is still a draft.";
                }
            }
            return await EarlierUnsubmittedRefusalAsync(run, people);
        }

        // No run for the previous month at all. That is fine for an org's very
        // first run, and a gap otherwise.
        var hasEarlier = await _runs.HasEarlierSubmittedRunAsync(run.PeriodYear, run.PeriodMonth);

        return hasEarlier
            ? $"Create and submit {PeriodLabel(prevYear, prevMonth)} first — there are earlier "
              + "submitted runs but no run for that month. Skipping it would freeze this "
              + "month's PCB, EPF and SOCSO against a zero year-to-date, and a submitted "
              + "payslip cannot be edited afterwards."
            : null;
    }

    // Per person, across the whole year — not just last month. Someone may be
    // in no run last month (another group's month) yet still have an earlier
    // run waiting: this run would be filed before it, freezing their
    // year-to-date without it and settling unpaid leave that run settles too.
    private async Task<string?> EarlierUnsubmittedRefusalAsync(PayrollRun run, HashSet<string>? people)
    {
        var earlier = (await _runs.GetAllAsync())
            .Where(r => r.PeriodYear == run.PeriodYear
                        && r.PeriodMonth < run.PeriodMonth
                        && r.Status != PayrollRunStatus.SUBMITTED)
            .OrderBy(r => r.PeriodMonth)
            .ToList();
        foreach (var other in earlier)
        {
            var theirs = await PeopleOfAsync(other);
            if (people is null || theirs is null || people.Overlaps(theirs))
            {
                return $"Submit {PeriodLabel(other.PeriodYear, other.PeriodMonth)} first — payroll runs are "
                       + "submitted in order, and that month's run for some of these people is still a draft.";
            }
        }
        return null;
    }

    // Who a run is for: its chosen roster AND whoever it has a payslip for
    // (someone on the roster but skipped — not yet joined, say — is still
    // its, and an imported or legacy run has payslips but no roster). Null
    // means everyone: a run with neither, which overlaps any other.
    private async Task<HashSet<string>?> PeopleOfAsync(PayrollRun run)
    {
        var people = (await _payslips.GetForRunAsync(run.Id))
            .Select(p => p.EmployeeProfileId)
            .Concat((await _runMembers.GetForRunAsync(run.Id)).Select(m => m.EmployeeProfileId))
            .ToHashSet(StringComparer.Ordinal);
        return people.Count > 0 ? people : null;
    }

    public async Task<PayrollRunSaveResult> ApproveAsync(string id)
    {
        var run = await _runs.GetByIdAsync(id);
        if (run is null) return NotFound();

        if (run.Status != PayrollRunStatus.PENDING_APPROVAL)
        {
            return Refused("Only a run awaiting approval can be approved.");
        }

        var now = DateTime.UtcNow;
        run.Status = PayrollRunStatus.SUBMITTED;
        run.SubmittedAt = now;
        // The APPROVER, not the proposer. Who put this month's pay live is the
        // question an audit asks, and SubmittedForApprovalById still records
        // who proposed it.
        run.SubmittedById = _currentUser.UserId;
        await _runs.UpdateAsync(run);

        // What this run docked or settled now counts; a later draft built
        // before this approval may have settled the same leave too.
        if (_settlements is not null)
        {
            await _settlements.SetFinalForRunsAsync([run.Id], true);
            if (_drafts is not null)
                await _drafts.MarkLaterDraftsAsync(new DateTime(run.PeriodYear, run.PeriodMonth, 1));
        }

        if (_transfers is not null)
            await _transfers.RecomputeCarriedYearToDateAsync(run.OrganizationId, run.PeriodYear);

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.PayrollRunApprove,
            $"Approved the {RunLabel(run)} payroll run",
            TargetType: "PayrollRun",
            TargetId: run.Id,
            Metadata: new
            {
                run.PeriodYear,
                run.PeriodMonth,
                run.SubmittedForApprovalById,
                run.TotalNet,
                run.TotalCostToEmployer,
            }));

        // Post to Xero, if the org asked for that. Deliberately AFTER the
        // approval is committed and deliberately best-effort: an approval
        // already given must not be undone because an accounting integration
        // was unreachable. The failure lands on the run's own error column.
        await _xeroSync.SyncOnApprovalAsync(run.Id);

        return new PayrollRunSaveResult(true, ToDto(run), null);
    }

    public async Task<PayrollRunSaveResult> RejectAsync(string id, string? reason)
    {
        var run = await _runs.GetByIdAsync(id);
        if (run is null) return NotFound();

        if (run.Status != PayrollRunStatus.PENDING_APPROVAL)
        {
            return Refused("Only a run awaiting approval can be sent back.");
        }

        run.Status = PayrollRunStatus.DRAFT;
        run.ApprovalRejectionReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        // The proposal is withdrawn, so who made it is no longer current.
        run.SubmittedForApprovalAt = null;
        run.SubmittedForApprovalById = null;
        await _runs.UpdateAsync(run);

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.PayrollRunRejectApproval,
            $"Sent the {RunLabel(run)} payroll run back to draft",
            TargetType: "PayrollRun",
            TargetId: run.Id,
            Metadata: new { run.PeriodYear, run.PeriodMonth, Reason = run.ApprovalRejectionReason }));

        return new PayrollRunSaveResult(true, ToDto(run), null);
    }

    // The LHDN receipts are paperwork about a payment already made, so only a
    // SUBMITTED month takes them. They change no figure: no staleness, no
    // regeneration, and a status change never clears them.
    public async Task<PayrollRunSaveResult> SetLhdnReceiptsAsync(string id, SetLhdnReceiptsDto dto)
    {
        var run = await _runs.GetByIdAsync(id);
        if (run is null) return NotFound();

        if (run.Status != PayrollRunStatus.SUBMITTED)
        {
            return Refused("LHDN receipts can only be recorded on an approved run.");
        }

        run.PcbReceiptNo = Blank(dto.PcbReceiptNo);
        run.PcbReceiptDate = dto.PcbReceiptDate?.Date;
        run.Cp38ReceiptNo = Blank(dto.Cp38ReceiptNo);
        run.Cp38ReceiptDate = dto.Cp38ReceiptDate?.Date;
        await _runs.UpdateAsync(run);

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.PayrollRunLhdnReceipts,
            $"Recorded the LHDN receipts for the {RunLabel(run)} payroll run",
            TargetType: "PayrollRun",
            TargetId: run.Id,
            Metadata: new
            {
                run.PeriodYear, run.PeriodMonth,
                run.PcbReceiptNo, run.PcbReceiptDate, run.Cp38ReceiptNo, run.Cp38ReceiptDate,
            }));

        return new PayrollRunSaveResult(true, ToDto(run), null);
    }

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public async Task<PayrollRunRevertResult> RevertToDraftAsync(string id)
    {
        var run = await _runs.GetByIdAsync(id);
        if (run is null) return new PayrollRunRevertResult(false, null, [], null);

        if (run.Status != PayrollRunStatus.SUBMITTED)
        {
            return new PayrollRunRevertResult(
                false, null, [], "Only a submitted run can be reverted to draft.");
        }

        // An imported month is history from the previous system, not a run
        // this one computed. Back in draft it would leave every later month's
        // year-to-date with a hole, cascade through the year, and a re-run
        // would replace the old system's figures with today's. Re-importing
        // is how it's corrected — the YTD import replaces imported months.
        if (run.Source == PayrollRunSource.IMPORTED)
        {
            return new PayrollRunRevertResult(
                false, null, [], ImportedRevertRefusal);
        }

        // THE cascade. Every later submitted month in the same year carries
        // YTD-cumulative figures — PCB's annualisation, the SOCSO/EIS relief —
        // computed off this month. Reverting this one alone would leave them
        // filed against a year-to-date that no longer exists.
        //
        // A later month awaiting approval was built on this month too — its
        // year-to-date, and any unpaid leave it settled for this month. It
        // goes back to draft, needing a re-run, rather than being approved
        // on figures that are about to change.
        var (later, pending) = await LaterRunsReachedAsync(run);
        var now = DateTime.UtcNow;
        foreach (var p in pending)
        {
            p.Status = PayrollRunStatus.DRAFT;
            p.SubmittedForApprovalAt = null;
            p.SubmittedForApprovalById = null;
            p.ApprovalRejectionReason = $"Sent back: the {RunLabel(run)} payroll run was reverted to draft.";
            p.LastMutatedAt = now;
        }

        var reverted = later.Append(run).ToList();
        foreach (var target in reverted) RevertOne(target);

        await _runs.UpdateManyAsync([.. reverted, .. pending]);

        // Back in draft, what they docked or settled is provisional again.
        if (_settlements is not null)
            await _settlements.SetFinalForRunsAsync(reverted.Select(r => r.Id).ToList(), false);

        // A later DRAFT was built on this month's figures too — its
        // year-to-date, and any unpaid leave it settled for this month — so
        // it must be re-run before it can be sent for approval.
        if (_drafts is not null)
            await _drafts.MarkLaterDraftsAsync(new DateTime(run.PeriodYear, run.PeriodMonth, 1));

        var alsoReverted = later.Concat(pending).Select(RunLabel).ToList();

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.PayrollRunRevertToDraft,
            $"Reverted the {RunLabel(run)} payroll run to draft"
            + (alsoReverted.Count == 0
                ? string.Empty
                : $", and with it {string.Join(", ", alsoReverted)}"),
            TargetType: "PayrollRun",
            TargetId: run.Id,
            Metadata: new { run.PeriodYear, run.PeriodMonth, AlsoReverted = alsoReverted }));

        if (_transfers is not null)
            await _transfers.RecomputeCarriedYearToDateAsync(run.OrganizationId, run.PeriodYear);

        return new PayrollRunRevertResult(true, ToDto(run), alsoReverted, null);
    }

    // Back to DRAFT, with the approval trail cleared so a re-submission records
    // who did it THIS time. LastMutatedAt is deliberately untouched: a status
    // change does not make payslips any more or less current than they were.
    private static void RevertOne(PayrollRun run)
    {
        // A month that was approved keeps the people it was approved with: a
        // re-run after a revert must not add someone who only moved onto one
        // of its policies later (the policy lookup is today's, not the
        // month's). So it stops growing for good.
        run.ScopePolicyIdsJson = null;
        run.ScopeExcludedProfileIdsJson = null;
        run.Status = PayrollRunStatus.DRAFT;
        run.SubmittedAt = null;
        run.SubmittedById = null;
        run.SubmittedForApprovalAt = null;
        run.SubmittedForApprovalById = null;
        run.ApprovalRejectionReason = null;
    }

    private const string ImportedRevertRefusal =
        "This month was imported from your previous payroll system, so it can't go back to draft. "
        + "To correct it, import it again under Payroll runs → Import earlier payroll — that replaces the imported month.";

    private const string ImportedGenerateRefusal =
        "This month was imported from your previous payroll system, so payroll can't be run on it. "
        + "To correct it, import it again under Payroll runs → Import earlier payroll — that replaces the imported month.";

    public async Task<IReadOnlyList<string>> GetRevertImpactAsync(string id)
    {
        var run = await _runs.GetByIdAsync(id);
        if (run is null || run.Status != PayrollRunStatus.SUBMITTED
            || run.Source == PayrollRunSource.IMPORTED) return [];

        var (later, pending) = await LaterRunsReachedAsync(run);
        return later.Concat(pending).Select(RunLabel).ToList();
    }

    // The later submitted (and awaiting-approval) runs in the year whose
    // year-to-date this run feeds, directly or through another: a run sharing
    // anyone with it, then anyone sharing with THAT run, and so on — split
    // September {Ali} reaches October {Ali, Ben}, which reaches November
    // {Ben}. Walked in month order, widening the people as runs are reached.
    // Year-to-date is per person, so a group never reached never read these
    // figures.
    private async Task<(List<PayrollRun> Submitted, List<PayrollRun> Pending)> LaterRunsReachedAsync(PayrollRun run)
    {
        // Imported months are left out: their figures are the previous
        // system's, typed in rather than computed off this year-to-date, so
        // reverting an earlier month doesn't make them wrong — and an imported
        // month must never go back to draft (see RevertToDraftAsync).
        var later = (await _runs.GetAllAsync())
            .Where(r => r.PeriodYear == run.PeriodYear
                        && r.PeriodMonth > run.PeriodMonth
                        && (r.Status == PayrollRunStatus.SUBMITTED || r.Status == PayrollRunStatus.PENDING_APPROVAL)
                        && r.Source != PayrollRunSource.IMPORTED)
            .OrderBy(r => r.PeriodMonth)
            .ToList();

        // Null = everyone (a run with no roster).
        var start = await PeopleOfAsync(run);
        HashSet<string>? people = start is null ? null : new HashSet<string>(start, StringComparer.Ordinal);
        var reached = new List<PayrollRun>();
        foreach (var other in later)
        {
            var theirs = await PeopleOfAsync(other);
            if (people is not null && theirs is not null && !people.Overlaps(theirs)) continue;

            reached.Add(other);
            if (theirs is null) people = null;
            else people?.UnionWith(theirs);
        }

        return (
            reached.Where(r => r.Status == PayrollRunStatus.SUBMITTED).ToList(),
            reached.Where(r => r.Status == PayrollRunStatus.PENDING_APPROVAL).ToList());
    }

    public async Task<PayrollRunSaveResult> DeleteDraftAsync(string id)
    {
        var run = await _runs.GetByIdAsync(id);
        if (run is null) return NotFound();

        // Only a draft. A submitted run has been filed, and a pending one is
        // somebody else's decision to make.
        if (run.Status != PayrollRunStatus.DRAFT)
        {
            return Refused("Only a draft run can be deleted. Revert it to draft first.");
        }

        // Clear what hangs off the run before the run itself. Passing empty
        // lists to ReplaceForRunAsync is the delete half of the same
        // transactional replace generation uses.
        await _payslips.ReplaceForRunAsync(run.Id, [], []);
        if (_settlements is not null) await _settlements.ReplaceForRunAsync(run.Id, []);
        await _adjustments.DeleteForRunAsync(run.Id);
        // Its chosen roster too — left behind, the rows would still count as
        // "this person is on a run this month" under the one-run-per-person rule.
        await _runMembers.ReplaceForRunAsync(run, []);
        // Detaching leaves the claims themselves untouched — they simply become
        // free to attach to another run.
        await _runClaims.DeleteForRunAsync(run.Id);

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.PayrollRunDelete,
            $"Deleted the {RunLabel(run)} draft payroll run",
            TargetType: "PayrollRun",
            TargetId: run.Id,
            Metadata: new { run.PeriodYear, run.PeriodMonth }));

        await _runs.DeleteAsync(run);

        return new PayrollRunSaveResult(true, null, null);
    }

    private static PayrollRunSaveResult NotFound() => new(false, null, null);

    private static PayrollRunSaveResult Refused(string error) => new(false, null, error);

    // ─── Who belongs on the run ─────────────────────────────────────────

    // Null means "include them". Everything else is a legitimate absence, not an
    // error — which is why the caller reports these rather than failing.
    // ─── Archived ≠ unpaid ──────────────────────────────────────────────
    //
    // Archiving records that someone has left; it does not cancel what they
    // earned before going. A person archived on the 15th — by hand, by the
    // past-leaver sweep the day after their leave date, or by a transfer to
    // another company — is still owed the 1st to their last day. So payroll
    // follows the LAST DAY, not the archive flag:
    //
    //   last day = their leave date; for an archived profile with none, the
    //              day it was archived.
    //
    // They stay on runs for any month up to and including that day (prorated
    // to it) and drop off after. Archived with neither date: nothing to go
    // on, so they're left out, as before.
    public static DateTime? LastPayableDay(EmployeeProfile p) =>
        p.LeaveDate ?? (p.IsArchived ? p.ArchivedAt?.Date : null);

    // Whether the profile can be on a run for this month. With no month (an
    // old caller), archived means no. Public so every service that decides
    // "is this person on the run" (claims, adjustment imports) agrees with
    // generation.
    public static bool PayableIn(EmployeeProfile p, int? year, int? month)
    {
        if (!p.IsArchived) return true;
        if (year is null || month is null) return false;
        return LastPayableDay(p) is { } last && last.Date >= new DateTime(year.Value, month.Value, 1);
    }

    private static string? SkipReasonFor(EmployeeProfile profile, PayrollRun run)
    {
        if (profile.IsArchived && LastPayableDay(profile) is null) return "Archived";

        // There is deliberately no "reported to LHDN" reason. The previous
        // system stored such a flag and kept paying; orgs migrated from it had
        // it ticked on current staff, and skipping on it left a whole company's
        // month with no payslips — so the flag was dropped. A leaver is left
        // out by their leave date below.

        // NOTE: incomplete profiles are excluded at SELECTION, not here — the
        // "Start a payroll run" picker only lists payable (complete) employees,
        // so an incomplete profile never enters a run's member scope in the
        // first place (see PayrollProfileReadiness + GetPickerAsync). Gating it
        // again at generation would also drop everyone from a legacy run that
        // has no member scope, which is not what this method is for.

        var calendarDays = PayPeriod.CalendarDaysInMonth(run.PeriodYear, run.PeriodMonth);
        var workedDays = PayPeriod.EffectiveWorkedDays(
            run.PeriodYear, run.PeriodMonth, profile.JoinDate, LastPayableDay(profile), calendarDays);

        // Joined after the period ended, or left before it started.
        return workedDays is null ? "Not employed during this period" : null;
    }

    // ─── Profile → calculator input ─────────────────────────────────────

    private static PayslipCalculator.Input BuildInput(
        EmployeeProfile profile,
        PayrollSettings settings,
        PayrollRun run,
        PayrollYtdTotals ytd,
        PayrollRunAdjustment? adjustment,
        IReadOnlyList<PayrollRunClaim> attachedClaims,
        EmployeePolicy? policy,
        HoursBucketsDto? hours,
        decimal unpaidLeaveDays,
        decimal loanRepayment,
        PayrollOvertimeHours.Hours overtime,
        IReadOnlyList<UnpaidLeaveCarryForward.Carry>? carries = null)
    {
        // The employee's prior-employer carryover (their TP3) folds into the YTD
        // figures, so a mid-year joiner is not over-withheld until December.
        //
        // A TP3 declares ONE calendar year (LHDN MTD Spec §10), so it only
        // applies to the run whose year it is tagged with. Without this gate a
        // figure declared for 2026 keeps inflating Y in 2027 and every year
        // after, over-withholding forever. A declaration with no year is not
        // attributable to any year and is ignored — the same answer v1 gives,
        // where the comparison is against a null.
        var carryApplies = profile.PrevEmploymentYear == run.PeriodYear;

        // `PrevIncludesPriorThisOrgPeriod` marks a rehire whose declared prev*
        // figures ALREADY include the months they worked here earlier this year.
        // Adding this org's own YTD on top would then double-count it.
        var carryOwnOrgYtd = !profile.PrevIncludesPriorThisOrgPeriod;

        // The previous employer's items with a yearly limit, carried into the
        // per-category YTD every exemption ceiling and TP1 limit reads.
        var previousItems = PreviousEmployerItems.Carry(
            carryApplies ? PreviousEmployerItems.Parse(profile.PrevByCategoryJson) : [],
            ytd.AllowanceByCategory,
            carryOwnOrgYtd);

        // Cash overtime needs hours — approved requests, or the admin's own
        // figure on the adjustment row (PayrollOvertimeHours decides which) —
        // and a policy that does not say otherwise.
        //
        // A policy can switch cash OT off two ways, and both are honoured: OT
        // disabled outright, or TIME_BANK, which credits time off for the same
        // hours — paying cash as well would pay them twice.
        //
        // The ABSENCE of a policy is not one of those ways, and this is a
        // deliberate divergence from the reference, which pays nothing without
        // one. Two reasons. The hours only exist because an admin typed them
        // or a supervisor approved them, so paying zero for them is an
        // underpayment with nothing on the payslip to explain it. And EA 1955 s.60A sets a floor an employer
        // does not opt into by configuring a policy — so with no policy the
        // calculator's statutory defaults apply, which is what its OtRate*
        // defaults already are.
        var cashOt = overtime.Any
                     && (policy is null || (policy.OtEnabled && policy.OtMethod == OtMethod.CASH));

        // Attendance figures only mean anything when the employee's policy puts
        // them on attendance at all. Otherwise the column reads "—" rather than
        // a confident zero, which for HOURLY staff would be a zero PAYSLIP.
        var attendanceApplies = policy?.CanAccessAttendance == true && hours is not null;

        // NORMAL minutes, not total worked: anything past the shift length sits
        // in BeyondShiftMin and only becomes money through an approved overtime
        // submission. Counting it here would pay it twice, once flat and once
        // at the OT multiplier.
        var autoWorkedHours = attendanceApplies ? hours!.NormalMin / 60m : (decimal?)null;
        var autoExpectedHours = attendanceApplies ? hours!.ExpectedMin / 60m : (decimal?)null;

        // The admin's per-run override always wins over what attendance derived.
        var workedHours = adjustment?.WorkedHours ?? autoWorkedHours;

        // Expected hours are a MONTHLY concept — an hourly employee is paid for
        // what they worked, with nothing to compare it against.
        var expectedHours = profile.SalaryType == SalaryType.MONTHLY
            ? adjustment?.ExpectedHours ?? autoExpectedHours
            : null;

        return new PayslipCalculator.Input
        {
            PeriodYear = run.PeriodYear,
            PeriodMonth = run.PeriodMonth,

            SalaryType = profile.SalaryType,
            MonthlySalary = profile.MonthlySalary,
            HourlyRate = profile.HourlyRate,
            // The profile's recurring rows with this run's overrides applied,
            // followed by this run's one-off rows. Merged into ONE list on
            // purpose: the calculator has a single category-aware routing loop,
            // and a one-off deduction has to obey `ReducesBase`, the exemption
            // ceilings and the six `SubjectTo*` flags exactly as a recurring one
            // does.
            FixedAllowances = WithLoanRepayment(
                [
                    .. WithUnpaidLeave(
                        PayrollRunAdjustments.Merge(
                            ParseFixedAllowances(profile.FixedAllowancesJson), adjustment),
                        profile, unpaidLeaveDays, run.PeriodYear, run.PeriodMonth),
                    .. (carries ?? []).Select(c => c.Row),
                ],
                loanRepayment),
            JoinDate = profile.JoinDate,
            LeaveDate = LastPayableDay(profile),
            SkipProration = run.SkipProration,

            Nationality = profile.Nationality,
            HasPr = profile.HasPr,
            IsResident = PayslipCalculator.IsTaxResident(profile.IsResident, profile.Nationality),
            IsOku = profile.IsOku,
            DateOfBirth = profile.DateOfBirth,
            SpecialTaxScheme = PayslipCalculator.SpecialTaxSchemeFor(
                profile.SpecialTaxScheme, profile.SpecialTaxFrom, profile.SpecialTaxTo,
                run.PeriodYear, run.PeriodMonth),

            ContributeToEpf = profile.ContributeToEpf,
            EpfMemberBefore1998 = profile.EpfMemberBefore1998,
            EpfEmployeeRate = profile.EpfEmployeeRate,
            EpfEmployeeVoluntary = profile.EpfEmployeeVoluntary,
            EpfEmployerVoluntary = profile.EpfEmployerVoluntary,

            SocsoScheme = profile.SocsoScheme,
            ContributeToEis = profile.ContributeToEis,
            ContributeToSkbbk = profile.ContributeToSkbbk,

            SpouseWorking = profile.SpouseWorking,
            SpouseDisabled = profile.SpouseDisabled,
            Children = ParseChildRelief(profile.ChildReliefJson),

            IncomeTaxNumber = profile.IncomeTaxNumber,
            EpfNumber = profile.EpfNumber,
            SocsoNumber = profile.SocsoNumber,

            WorkingDaysRule = settings.WorkingDaysRule,
            DefaultEpfEmployeeRate = settings.DefaultEpfEmployeeRate,
            HrdfEnabled = settings.HrdfEnabled,
            HrdfRate = settings.HrdfRate,
            HrdfOverride = adjustment?.HrdfLevy,
            AutoApplySocsoEisRelief = settings.AutoApplySocsoEisRelief,

            // ---- Overtime ----
            // Hours are the approved requests' unless the admin typed their own
            // (PayrollOvertimeHours); the multipliers are the policy's. Both or
            // neither: `cashOt` is false
            // when the policy banks overtime as time off or disables it
            // outright, and paying cash for hours already banked would pay them
            // twice.
            OtNormalHours = cashOt ? overtime.Normal : 0m,
            OtRestHours = cashOt ? overtime.Rest : 0m,
            OtPublicHours = cashOt ? overtime.PublicHoliday : 0m,

            // Left at the calculator's statutory defaults (EA 1955 s.60A) when
            // the employee has no policy — the floor the Act sets, not zero.
            OtRateNormal = policy?.OtRateNormalDay ?? 1.5m,
            OtRateRest = policy?.OtRateRestDay ?? 2.0m,
            OtRatePublicHoliday = policy?.OtRatePublicHoliday ?? 3.0m,

            // ---- Attendance ----
            // Derived from the period's attendance, overridable per run.
            // Display-only for MONTHLY staff, whose pay is day-based — but for
            // HOURLY staff WorkedHours IS the paid quantity.
            WorkedHours = workedHours,
            ExpectedHours = expectedHours,
            UnpaidLeaveDays = unpaidLeaveDays > 0m ? unpaidLeaveDays : null,

            // ---- Attached claims ----
            // Each becomes a REIMBURSEMENT line carrying its claim id, so the
            // rebuilt line item still points back at what it is paying.
            // Snapshotted amounts, not the claims' current ones.
            Reimbursements = attachedClaims
                .Select(c => new PayslipCalculator.Reimbursement(c.ClaimId, c.Label, c.Amount))
                .ToList(),

            YtdTaxable = ytd.Taxable
                + Carry(carryApplies, profile.PrevRemuneration, carryOwnOrgYtd, ytd.Taxable),
            YtdEpf = ytd.Epf + Carry(carryApplies, profile.PrevEpf, carryOwnOrgYtd, ytd.Epf),
            YtdPcb = ytd.Pcb + Carry(carryApplies, profile.PrevPcb, carryOwnOrgYtd, ytd.Pcb),
            YtdZakat = ytd.Zakat + Carry(carryApplies, profile.PrevZakat, carryOwnOrgYtd, ytd.Zakat),
            YtdSocsoEis = ytd.SocsoEis,
            // ΣLP: this org's granted TP1 reliefs, the previous employer's
            // itemised ones, and whatever of theirs was declared only as a total.
            YtdAllowableDeductions = ytd.AllowableDeductions
                + Carry(carryApplies, profile.PrevAllowableDeductions,
                        carryOwnOrgYtd, ytd.AllowableDeductions)
                + previousItems.Tp1Relief,
            YtdAllowanceByCategory = previousItems.ByCategory,
        };
    }

    // Approved unpaid leave, docked as its own line rather than by shrinking
    // the salary — so the payslip says why someone was paid less.
    //
    // Appended as an ordinary category row so it goes through the same routing
    // as everything else: `deduct_unpaid_leave` reduces the statutory bases as
    // well as gross, because the wage was never earned. The category is marked
    // SkipProration — the amount is already stated at the full daily rate, so
    // prorating a joiner's deduction as well would dock them twice.
    private static IReadOnlyList<FixedAllowance> WithUnpaidLeave(
        IReadOnlyList<FixedAllowance> rows,
        EmployeeProfile profile,
        decimal unpaidLeaveDays,
        int year,
        int month)
    {
        if (profile.SalaryType != SalaryType.MONTHLY) return rows;

        var amount = PayPeriod.UnpaidLeaveDeduction(
            profile.MonthlySalary, unpaidLeaveDays, year, month);

        if (amount <= 0m) return rows;

        return [.. rows, new FixedAllowance
        {
            Category = PayrollAdjustmentCategories.DeductUnpaidLeave,
            Name = "Unpaid Leave",
            Amount = amount,
        }];
    }

    // Earlier months of this year, filed here, whose unpaid leave has changed
    // since — see UnpaidLeaveCarryForward. Keyed by employee profile.
    private async Task<IReadOnlyDictionary<string, IReadOnlyList<SettledMonth>>>
        UnpaidLeaveCarriesAsync(PayrollRun run, IReadOnlyCollection<EmployeeProfile> profiles)
    {
        var none = new Dictionary<string, IReadOnlyList<SettledMonth>>(StringComparer.Ordinal);
        if (_settlements is null || run.PeriodMonth == 1 || profiles.Count == 0) return none;

        // Only months filed HERE: an imported month's figures came from the
        // previous system, which may already have docked the leave.
        var wanted = profiles.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var filed = (await _payslips.GetEarlierInYearAsync(run.PeriodYear, run.PeriodMonth))
            .Where(x => x.Run.Status == PayrollRunStatus.SUBMITTED
                        && x.Run.Source == PayrollRunSource.COMPUTED
                        && x.Payslip.SnapshotSalaryType == SalaryType.MONTHLY
                        && wanted.Contains(x.Payslip.EmployeeProfileId))
            .ToList();
        if (filed.Count == 0) return none;

        // A month whose unpaid days were typed on the run replaced the Leave
        // module's figure on purpose — it isn't second-guessed.
        var typed = (await _adjustments.GetForRunsAsync(filed.Select(f => f.Run.Id).Distinct().ToList()))
            .Where(a => a.UnpaidLeaveDays is not null)
            .Select(a => (a.PayrollRunId, a.EmployeeProfileId))
            .ToHashSet();
        filed = filed.Where(f => !typed.Contains((f.Run.Id, f.Payslip.EmployeeProfileId))).ToList();

        var settled = (await _settlements.GetFinalForYearAsync(run.PeriodYear))
            .GroupBy(s => (s.EmployeeProfileId, s.PeriodMonth))
            .ToDictionary(g => g.Key, g => g.ToList());

        var partsByMonth = new Dictionary<int, ILookup<string, UnpaidLeavePartDto>>();
        foreach (var month in filed.Select(f => f.Run.PeriodMonth).Distinct())
        {
            var start = new DateTime(run.PeriodYear, month, 1);
            partsByMonth[month] = (await _leave.GetLeavePartsForOrgAsync(start, start.AddMonths(1).AddDays(-1)))
                .ToLookup(p => p.UserId, StringComparer.Ordinal);
        }

        return filed
            .GroupBy(f => f.Payslip.EmployeeProfileId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<SettledMonth>)g
                    .OrderBy(f => f.Run.PeriodMonth)
                    .Select(f =>
                    {
                        var result = UnpaidLeaveCarryForward.For(new UnpaidLeaveCarryForward.FiledMonth(
                            run.PeriodYear, f.Run.PeriodMonth,
                            f.Payslip.SnapshotMonthlySalary ?? 0m,
                            f.Payslip.CreatedAt,
                            f.Payslip.TracksUnpaidLeave,
                            [.. partsByMonth[f.Run.PeriodMonth][f.Payslip.UserId]],
                            settled.GetValueOrDefault((g.Key, f.Run.PeriodMonth)) ?? []));
                        return new SettledMonth(run.PeriodYear, f.Run.PeriodMonth, result.Lines, result.Owed);
                    })
                    .ToList(),
                StringComparer.Ordinal);
    }

    // One filed month's carry lines, and every application it settles.
    private sealed record SettledMonth(
        int Year, int Month,
        IReadOnlyList<UnpaidLeaveCarryForward.Carry> Lines,
        IReadOnlyList<UnpaidLeaveCarryForward.Owed> Owed);

    private static UnpaidLeaveSettlement Settlement(
        PayrollRun run, Payslip payslip, int year, int month, string applicationId, decimal days,
        UnpaidLeavePartDto? part, UnpaidLeaveSettlement? last, DateTime now) => new()
        {
            OrganizationId = run.OrganizationId,
            PayrollRunId = run.Id,
            PayslipId = payslip.Id,
            EmployeeProfileId = payslip.EmployeeProfileId,
            LeaveApplicationId = applicationId,
            PeriodYear = year,
            PeriodMonth = month,
            Days = days,
            // The leave as it is now.
            // A leave no longer in that month records no type or dates, so if
            // it's moved back later it reads as changed and is docked again.
            LeaveTypeId = part?.LeaveTypeId ?? string.Empty,
            LeaveStartDate = part?.StartDate ?? default,
            LeaveEndDate = part?.EndDate ?? default,
            CreatedAt = now,
        };

    // Marks the line that settled a carry with the month and days it settled,
    // so a later run doesn't settle them again.
    private static PayslipLineItem WithCarry(
        PayslipLineItem line, IReadOnlyList<UnpaidLeaveCarryForward.Carry> carries)
    {
        var carry = carries.FirstOrDefault(c =>
            c.Row.Name == line.Label && c.Row.Category == line.Category);
        if (carry is null) return line;

        line.CarriedFromYear = carry.Year;
        line.CarriedFromMonth = carry.Month;
        line.CarriedDays = carry.Days;
        return line;
    }

    // A staff loan's installment for this period, as a deduction row.
    //
    // It goes through the same merged list as everything else so it obeys its
    // category's flags — `deduct_loan_repayment` does NOT reduce the statutory
    // bases, because repaying a loan is the employee spending money they
    // earned, not earning less. EPF, SOCSO, EIS and PCB are all computed on
    // the wage before it.
    private static IReadOnlyList<FixedAllowance> WithLoanRepayment(
        IReadOnlyList<FixedAllowance> rows, decimal repayment)
    {
        if (repayment <= 0m) return rows;

        return [.. rows, new FixedAllowance
        {
            Category = PayrollAdjustmentCategories.DeductLoanRepayment,
            Name = "Loan Repayment",
            Amount = repayment,
        }];
    }

    // How much of a declared prev* figure to add on top of this org's own YTD.
    //
    // Normally all of it — the figure is strictly the previous employer's. On a
    // rehire flagged `PrevIncludesPriorThisOrgPeriod` the declared total already
    // covers the months worked here, so this org's YTD is subtracted back out
    // before adding, and the result is floored at zero rather than credited.
    private static decimal Carry(
        bool applies, decimal? declared, bool isPriorEmployerOnly, decimal ownOrgYtd)
    {
        if (!applies || declared is null or <= 0m) return 0m;

        return isPriorEmployerOnly
            ? declared.Value
            : Math.Max(0m, declared.Value - ownOrgYtd);
    }

    private static IReadOnlyList<FixedAllowance> ParseFixedAllowances(string? json) =>
        Parse<FixedAllowance>(json);

    // Lenient on purpose: migrated profiles carry v1's legacy values, and a
    // strict read drops every child (see ChildReliefJson).
    private static IReadOnlyList<ChildRelief> ParseChildRelief(string? json) =>
        ChildReliefJson.Parse(json);

    // Malformed JSON reads as an empty list. These columns are free-form and
    // written by another system: one bad row must not take a month's payroll
    // down, and an empty list under-claims rather than over-claims.
    private static IReadOnlyList<T> Parse<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<T>>(json, ProfileJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // ─── Result → rows ──────────────────────────────────────────────────

    private static Payslip ToPayslip(
        PayrollRun run,
        EmployeeProfile profile,
        string name,
        OrganizationMembership? membership,
        PayslipCalculator.Result r,
        DateTime now) => new()
        {
            PayrollRunId = run.Id,
            EmployeeProfileId = profile.Id,
            PeriodYear = run.PeriodYear,
            PeriodMonth = run.PeriodMonth,
            UserId = profile.UserId,

            SnapshotName = name,
            SnapshotEmployeeNumber = membership?.EmployeeNumber,
            SnapshotPosition = membership?.JobTitle,
            SnapshotNationality = profile.Nationality,
            SnapshotIsResident = PayslipCalculator.IsTaxResident(profile.IsResident, profile.Nationality),

            SnapshotSalaryType = profile.SalaryType,
            SnapshotMonthlySalary = profile.MonthlySalary,
            SnapshotHourlyRate = profile.HourlyRate,
            SnapshotEpfRatesJson = JsonSerializer.Serialize(r.EpfRates, SnapshotJson),

            TotalWorkingDays = r.TotalWorkingDays,
            ProratedDays = r.ProratedDays,
            ProrationDaysInPeriod = r.ProrationDaysInPeriod,
            ProratedFactor = r.ProratedFactor,
            WorkedHours = r.WorkedHours,
            ExpectedHours = r.ExpectedHours,
            UnpaidLeaveDays = r.UnpaidLeaveDays,

            BasicPay = r.BasicPay,
            ProratedPay = r.ProratedPay,
            OtNormalHours = r.OtNormalHours,
            OtRestHours = r.OtRestHours,
            OtPublicHours = r.OtPublicHours,
            OtPay = r.OtPay,
            TotalAllowances = r.TotalAllowances,
            TotalReimbursements = r.TotalReimbursements,
            TotalDeductions = r.TotalDeductions,
            TotalBenefitsInKind = r.TotalBenefitsInKind,

            EpfEmployee = r.EpfEmployee,
            EpfEmployer = r.EpfEmployer,
            SocsoEmployee = r.SocsoEmployee,
            SocsoEmployer = r.SocsoEmployer,
            EisEmployee = r.EisEmployee,
            EisEmployer = r.EisEmployer,
            SkbbkEmployee = r.SkbbkEmployee,
            SkbbkWage = r.SkbbkWage,
            Pcb = r.Pcb,
            PcbNormal = r.PcbNormal,
            PcbAdditional = r.PcbAdditional,
            PcbCalculationJson = JsonSerializer.Serialize(r.PcbCalculation, SnapshotJson),
            Cp38 = r.Cp38,
            VoluntaryPcb = r.VoluntaryPcb,
            Zakat = r.Zakat,
            Hrdf = r.Hrdf,
            HrdfWage = r.HrdfWage,

            GrossPay = r.GrossPay,
            NetPay = r.NetPay,
            TotalCostToEmployer = r.TotalCostToEmployer,

            StatutoryWarnings = r.StatutoryWarnings.Count == 0
                ? null
                : string.Join(',', r.StatutoryWarnings),

            CreatedAt = now,
            UpdatedAt = now,
        };

    private static PayslipLineItem ToLineItem(
        string payslipId, PayslipCalculator.LineItem li, DateTime now, int sortOrder) => new()
        {
            PayslipId = payslipId,
            SortOrder = sortOrder,
            Kind = li.Kind,
            Label = li.Label,
            Amount = li.Amount,
            Category = li.Category,
            PcbTaxableAmount = li.PcbTaxableAmount,
            ClaimId = li.ClaimId,
            SubjectToEpf = li.SubjectToEpf,
            SubjectToSocso = li.SubjectToSocso,
            SubjectToEis = li.SubjectToEis,
            SubjectToPcb = li.SubjectToPcb,
            CreatedAt = now,
        };

    // Denormalised sums so the runs list renders without loading every payslip.
    // Recomputed wholesale on each generation rather than adjusted, because a
    // regeneration replaces the payslips it is summing.
    private static void ApplyTotals(PayrollRun run, List<Payslip> payslips)
    {
        run.EmployeeCount = payslips.Count;

        run.TotalGross = payslips.Sum(p => p.GrossPay);
        run.TotalNet = payslips.Sum(p => p.NetPay);
        run.TotalEmployeeEpf = payslips.Sum(p => p.EpfEmployee);
        run.TotalEmployerEpf = payslips.Sum(p => p.EpfEmployer);
        run.TotalEmployeeSocso = payslips.Sum(p => p.SocsoEmployee);
        run.TotalEmployerSocso = payslips.Sum(p => p.SocsoEmployer);
        run.TotalEmployeeEis = payslips.Sum(p => p.EisEmployee);
        run.TotalEmployerEis = payslips.Sum(p => p.EisEmployer);
        run.TotalEmployeeSkbbk = payslips.Sum(p => p.SkbbkEmployee);
        run.TotalPcb = payslips.Sum(p => p.Pcb);
        run.TotalCp38 = payslips.Sum(p => p.Cp38);
        run.TotalZakat = payslips.Sum(p => p.Zakat);

        run.TotalHrdf = payslips.Sum(p => p.Hrdf);
        // The levy return asks for the headcount and the wage base it was
        // charged on, so both are counted off the wage rather than the levy —
        // a zero-wage employee is not "subject to" it.
        run.EmployeesSubjectToHrdf = payslips.Count(p => p.HrdfWage > 0m);
        run.TotalWagesSubjectToHrdf = payslips.Sum(p => p.HrdfWage);

        run.TotalCostToEmployer = payslips.Sum(p => p.TotalCostToEmployer);
    }

    // ─── Mapping out ────────────────────────────────────────────────────

    private static PayrollRunDetailDto BuildDetail(
        PayrollRun run,
        IReadOnlyList<Payslip> payslips,
        IReadOnlyList<PayslipLineItem> lineItems,
        List<string> memberIds)
    {
        var byPayslip = lineItems
            .GroupBy(li => li.PayslipId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        return new PayrollRunDetailDto
        {
            Run = ToDto(run),
            Payslips = payslips
                .Select(p => PayslipMapper.ToDto(
                    p,
                    byPayslip.TryGetValue(p.Id, out var items) ? items : []))
                .ToList(),
            MemberEmployeeProfileIds = memberIds,
        };
    }

    private static PayrollRunDto ToDto(PayrollRun r) => new()
    {
        Id = r.Id,
        PeriodYear = r.PeriodYear,
        PeriodMonth = r.PeriodMonth,
        PeriodLabel = PeriodLabel(r.PeriodYear, r.PeriodMonth),
        Name = r.Name,
        Label = RunLabel(r),
        RunNumber = r.RunNumber,
        Status = r.Status,
        Source = r.Source,
        SkipProration = r.SkipProration,
        EmployeeCount = r.EmployeeCount,
        TotalGross = r.TotalGross,
        TotalGrossShown = PayrollGrossShown.Of(r),
        TotalNet = r.TotalNet,
        TotalEmployeeEpf = r.TotalEmployeeEpf,
        TotalEmployerEpf = r.TotalEmployerEpf,
        TotalEmployeeSocso = r.TotalEmployeeSocso,
        TotalEmployerSocso = r.TotalEmployerSocso,
        TotalEmployeeEis = r.TotalEmployeeEis,
        TotalEmployerEis = r.TotalEmployerEis,
        TotalEmployeeSkbbk = r.TotalEmployeeSkbbk,
        TotalPcb = r.TotalPcb,
        TotalCp38 = r.TotalCp38,
        TotalZakat = r.TotalZakat,
        TotalHrdf = r.TotalHrdf,
        EmployeesSubjectToHrdf = r.EmployeesSubjectToHrdf,
        TotalWagesSubjectToHrdf = r.TotalWagesSubjectToHrdf,
        TotalCostToEmployer = r.TotalCostToEmployer,
        GeneratedAt = r.GeneratedAt,
        SubmittedForApprovalAt = r.SubmittedForApprovalAt,
        SubmittedForApprovalById = r.SubmittedForApprovalById,
        SubmittedAt = r.SubmittedAt,
        SubmittedById = r.SubmittedById,
        ApprovalRejectionReason = r.ApprovalRejectionReason,
        // Stale = the inputs moved after the payslips were built. DRAFT only: a
        // run awaiting approval or filed can carry the stamp (see
        // MarkDraftsMutatedAsync) but isn't asked to re-run until it's back
        // in draft.
        IsStale = r.Status == PayrollRunStatus.DRAFT
                  && r.LastMutatedAt is not null
                  && (r.GeneratedAt is null || r.LastMutatedAt > r.GeneratedAt),
        PcbReceiptNo = r.PcbReceiptNo,
        PcbReceiptDate = r.PcbReceiptDate,
        Cp38ReceiptNo = r.Cp38ReceiptNo,
        Cp38ReceiptDate = r.Cp38ReceiptDate,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt,
    };


    private static string PeriodLabel(int year, int month) =>
        PayrollPeriodLabel.For(year, month);

    private static string RunLabel(PayrollRun run) => PayrollPeriodLabel.ForRun(run);
}
