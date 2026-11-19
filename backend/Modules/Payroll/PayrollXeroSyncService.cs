using System.Text.Json;
using AltomateHR.Api.Modules.Accounts;
using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Claims;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Projects;
using AltomateHR.Api.Modules.Teams;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Modules.Policies;
using AltomateHR.Api.Modules.Xero;
using AltomateHR.Api.Modules.Xero.Dtos;

namespace AltomateHR.Api.Modules.Payroll;

// Posting a payroll run to Xero as one manual journal.
//
// The arithmetic lives in `PayrollJournal`, which is pure and does the
// balancing. This orchestrates: load, build, post, record what happened.
//
// Two properties matter more than anything else here:
//
//   1. **A run posts at most once.** `PayrollRun.XeroManualJournalId` is the
//      gate and is uniquely indexed. On top of that, the Idempotency-Key sent
//      to Xero is derived from the run id and the payload, so even a retry
//      after a network timeout — where we never learned the first attempt
//      succeeded — returns the original journal rather than creating a second.
//
//   2. **A failure never blocks the payroll.** Approving a month is a payroll
//      decision; whether an accounting integration was reachable at that
//      second is not. `SyncOnApprovalAsync` records the failure and returns.
public class PayrollXeroSyncService : IPayrollXeroSyncService
{
    private readonly IPayrollRunRepository _runs;
    private readonly IPayslipRepository _payslips;
    private readonly IPayrollSettingsService _settings;
    private readonly IChartOfAccountRepository _accounts;
    private readonly IClaimsService _claims;
    private readonly IXeroService _xero;
    private readonly IAuditService _audit;
    private readonly IEmployeeProfileRepository _profiles;
    private readonly ILogger<PayrollXeroSyncService> _log;

    // Who is where, for the AI split's people rules (project, job title,
    // employment type, role, policy).
    private readonly ITeamRepository? _teams;
    private readonly ITeamMembershipRepository? _memberships;
    private readonly IProjectRepository? _projects;
    private readonly IDirectoryService? _directory;
    private readonly IPolicyService? _policies;

    public PayrollXeroSyncService(
        IPayrollRunRepository runs,
        IPayslipRepository payslips,
        IPayrollSettingsService settings,
        IChartOfAccountRepository accounts,
        IClaimsService claims,
        IXeroService xero,
        IAuditService audit,
        IEmployeeProfileRepository profiles,
        ILogger<PayrollXeroSyncService> log,
        // Optional so tests that never split by them needn't build them;
        // without one, that detail reads as blank and its rules match nobody.
        ITeamRepository? teams = null,
        ITeamMembershipRepository? memberships = null,
        IProjectRepository? projects = null,
        IDirectoryService? directory = null,
        IPolicyService? policies = null)
    {
        _directory = directory;
        _policies = policies;
        _profiles = profiles;
        _teams = teams;
        _memberships = memberships;
        _projects = projects;
        _runs = runs;
        _payslips = payslips;
        _settings = settings;
        _accounts = accounts;
        _claims = claims;
        _xero = xero;
        _audit = audit;
        _log = log;
    }

    // What the admin sees before committing: the exact lines that would post,
    // or the reason they cannot. Reads nothing from Xero beyond the tracking
    // categories and writes nothing at all.
    public async Task<PayrollXeroPreview?> PreviewAsync(string runId)
    {
        var input = await BuildInputAsync(runId);
        if (input is null) return null;

        var result = BuildJournal(input.Value.Input, input.Value.Context);

        return new PayrollXeroPreview(
            result.Ok,
            result.Error,
            result.Narration,
            result.Date,
            [.. result.Lines.Select(PreviewLine)],
            input.Value.Run.XeroManualJournalId,
            input.Value.Run.XeroSyncStatus,
            input.Value.Input.Mapping.JournalMode,
            result.Checks);
    }

    private static PayrollXeroPreviewLine PreviewLine(PayrollJournal.Line l) =>
        new(l.AccountCode, l.Description, l.Amount,
            l.AllTags.Count == 0 ? null : string.Join(" · ", l.AllTags.Select(t => t.Option)),
            l.Source);

    // The journal that would post: the standard one, then — when the org has
    // switched the AI split on — the same journal with its debits re-divided.
    private sealed record Journal(
        bool Ok, string? Error, string Narration, DateTime Date,
        IReadOnlyList<PayrollJournal.Line> Lines,
        IReadOnlyList<PayrollJournalSplit.Check>? Checks,
        IReadOnlyList<PayrollJournalSplit.Assignment>? Assignments = null);

    private static Journal BuildJournal(PayrollJournal.Input input, PayrollJournalSplit.Context context) =>
        BuildJournal(input, context, input.Mapping.JournalMode == XeroJournalMode.AI_SPLIT
            ? input.Mapping.SplitRules
            : null);

    private static Journal BuildJournal(
        PayrollJournal.Input input, PayrollJournalSplit.Context context,
        IReadOnlyList<PayrollJournalSplitRule>? rules)
    {
        var standard = PayrollJournal.Build(input);
        if (rules is null)
        {
            return new Journal(standard.Ok, standard.Error, standard.Narration, standard.Date, standard.Lines, null);
        }

        var split = PayrollJournalSplit.Apply(standard, input.Rows, rules, context);
        return new Journal(
            split.Ok, split.Error, standard.Narration, standard.Date, split.Lines, split.Checks, split.Assignments);
    }

    // The settings page's try-out: these rules (saved or just generated)
    // against a real run — the one asked for, else the latest generated one.
    // Writes nothing.
    public async Task<PayrollXeroSplitPreview> PreviewSplitAsync(
        string? runId, IReadOnlyList<PayrollJournalSplitRule> rules)
    {
        var id = runId ?? await LatestRunWithPayslipsAsync();
        var loaded = id is null ? null : await BuildInputAsync(id, withSplitContext: true);

        if (loaded is null)
        {
            var context = await SplitContextAsync([]);
            var checks = PayrollJournalSplit.CheckRules(rules, context);
            return new PayrollXeroSplitPreview(
                checks.All(c => c.Passed), null, null, [], [], checks, []);
        }

        var (run, input, ctx) = loaded.Value;
        var standard = PayrollJournal.Build(input);
        var journal = BuildJournal(input, ctx, rules);

        // What the split replaces, so the page can show before and after.
        var sources = rules.Select(r => r.Source).ToHashSet(StringComparer.Ordinal);
        var replaced = standard.Lines.Where(l => l.Source is { } s && sources.Contains(s)).ToList();

        return new PayrollXeroSplitPreview(
            journal.Ok,
            run.Id,
            PayrollPeriodLabel.ForRun(run),
            [.. replaced.Select(PreviewLine)],
            [.. journal.Lines.Select(PreviewLine)],
            journal.Checks ?? [],
            [.. (journal.Assignments ?? []).Select(a => new PayrollXeroSplitAssignment(
                a.EmployeeName,
                [.. a.Placements.Select(pl => new PayrollXeroSplitPlacement(
                    PayrollJournalSplitSources.Label(pl.Source),
                    pl.Category is null ? null : PayrollAdjustmentCategories.Find(pl.Category)?.Label ?? pl.Category,
                    pl.Rule, pl.AccountCode, pl.Amount))]))]);
    }

    // What a split prompt may refer to: accounts, tracking, departments and
    // the staff of the latest run, for the AI and for the checks.
    public async Task<PayrollXeroSplitOrgContext> GetSplitOrgContextAsync()
    {
        var latest = await LatestRunWithPayslipsAsync();
        var payslips = latest is null ? [] : await _payslips.GetForRunAsync(latest);
        var context = await SplitContextAsync(payslips);
        var accounts = await _accounts.GetAllAsync();

        // The standard mapping's account per splittable slot, so the AI can
        // see what each line posts to today ("the default salary account").
        var mapping = ParseMapping((await _settings.GetEffectiveAsync()).XeroMappingJson);
        var codeById = accounts.Where(a => !string.IsNullOrWhiteSpace(a.Code))
            .ToDictionary(a => a.Id, a => a.Code, StringComparer.Ordinal);
        var standard = PayrollJournalSplitSources.All
            .Select(source => (source, slot: source == PayrollJournalSplitSources.Overtime
                ? PayrollXeroAccounts.Allowance
                : source))
            .Select(x => (x.source, code: mapping.Accounts.GetValueOrDefault(x.slot) is { } id
                ? codeById.GetValueOrDefault(id)
                : null))
            .ToDictionary(x => x.source, x => x.code, StringComparer.Ordinal);

        return new PayrollXeroSplitOrgContext(
            context,
            mapping.SplitRules,
            standard,
            [.. accounts
                .Where(a => !a.IsArchived && !string.IsNullOrWhiteSpace(a.Code))
                .OrderBy(a => a.Code, StringComparer.Ordinal)
                .Select(a => new PayrollXeroSplitAccount(a.Code, a.Name, a.XeroType ?? a.Type))]);
    }

    private async Task<string?> LatestRunWithPayslipsAsync()
    {
        var runs = (await _runs.GetAllAsync())
            .OrderByDescending(r => r.PeriodYear)
            .ThenByDescending(r => r.PeriodMonth)
            .ThenByDescending(r => r.RunNumber)
            .Take(6);

        foreach (var run in runs)
        {
            if ((await _payslips.GetForRunAsync(run.Id)).Count > 0) return run.Id;
        }

        return null;
    }

    private async Task<PayrollJournalSplit.Context> SplitContextAsync(IEnumerable<Entities.Payslip> payslips)
    {
        var accounts = await _accounts.GetAllAsync();
        var active = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in accounts.Where(a => !a.IsArchived && !string.IsNullOrWhiteSpace(a.Code)))
        {
            active.TryAdd(a.Code.Trim(), $"{a.Code} · {a.Name}");
        }

        var tracking = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in await GetTrackingCategoriesAsync())
        {
            tracking.TryAdd(c.Name, c.Options.ToHashSet(StringComparer.Ordinal));
        }

        var people = await PeopleAsync();

        var employees = people.Keys.ToDictionary(id => id, _ => string.Empty, StringComparer.Ordinal);
        foreach (var p in payslips) employees[p.EmployeeProfileId] = p.SnapshotName;

        // The values that exist now, for the AI to choose from and the checks
        // to hold the rules to.
        IReadOnlySet<string> Values(Func<Person, IEnumerable<string?>> pick) =>
            people.Values.SelectMany(pick)
                .Select(v => v?.Trim())
                .Where(v => !string.IsNullOrEmpty(v))
                .ToHashSet(StringComparer.OrdinalIgnoreCase)!;
        var projectsWithTeams = await ProjectsWithTeamsAsync();

        return new PayrollJournalSplit.Context
        {
            ActiveAccounts = active,
            TrackingCategories = tracking,
            Departments = Values(p => [p.Department]),
            // Live projects with a team, members or not, so a rule can name a
            // project before anyone is in it.
            Projects = projectsWithTeams,
            JobTitles = Values(p => [p.JobTitle]),
            EmploymentTypes = Values(p => [p.EmploymentType]),
            Roles = Values(p => [p.Role]),
            Policies = Values(p => [p.Policy]),
            Employees = employees,
            EmployeeProjects = payslips
                .GroupBy(p => p.EmployeeProfileId, StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => people.GetValueOrDefault(g.Key)?.Projects ?? (IReadOnlyList<string>)[],
                    StringComparer.Ordinal),
        };
    }

    // What a people rule can match on, per payroll profile — read live, like
    // the run's other identifiers, so a split follows who is where now.
    private sealed record Person(
        string? Department, IReadOnlyList<string> Projects, string? JobTitle,
        string? EmploymentType, string? Role, string? Policy);

    private async Task<Dictionary<string, Person>> PeopleAsync()
    {
        var profiles = await _profiles.GetAllForCurrentOrgAsync();
        var userIds = profiles.Select(p => p.UserId).Distinct(StringComparer.Ordinal).ToList();

        var memberships = _directory is null
            ? new Dictionary<string, Employees.Entities.OrganizationMembership>(StringComparer.Ordinal)
            : (await _directory.GetMembershipsForCurrentOrgAsync())
                .GroupBy(m => m.UserId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var projects = await ProjectsByUserAsync();
        var policies = _policies is null
            ? new Dictionary<string, Policies.Entities.EmployeePolicy>(StringComparer.Ordinal)
            : await _policies.GetEffectivePoliciesForEmployeesAsync(userIds);

        return profiles.ToDictionary(p => p.Id, p =>
        {
            var membership = memberships.GetValueOrDefault(p.UserId);
            return new Person(
                p.Department,
                projects.GetValueOrDefault(p.UserId) ?? [],
                membership?.JobTitle,
                p.EmploymentStatus?.ToString(),
                membership?.Role,
                policies.GetValueOrDefault(p.UserId)?.Name);
        }, StringComparer.Ordinal);
    }

    // Live projects that have at least one team, members or not.
    private async Task<HashSet<string>> ProjectsWithTeamsAsync()
    {
        if (_teams is null || _projects is null) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var teamProjects = (await _teams.GetAllAsync()).Select(t => t.ProjectId).ToHashSet(StringComparer.Ordinal);
        return (await _projects.GetAllAsync())
            .Where(p => !p.IsArchived && !string.IsNullOrWhiteSpace(p.Name) && teamProjects.Contains(p.Id))
            .Select(p => p.Name.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    // User id → the names of the projects their teams belong to. Live
    // projects only; a team under an archived project doesn't count.
    private async Task<Dictionary<string, IReadOnlyList<string>>> ProjectsByUserAsync()
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        if (_teams is null || _memberships is null || _projects is null) return result;

        var projectNames = (await _projects.GetAllAsync())
            .Where(p => !p.IsArchived && !string.IsNullOrWhiteSpace(p.Name))
            .ToDictionary(p => p.Id, p => p.Name.Trim(), StringComparer.Ordinal);
        var projectByTeam = (await _teams.GetAllAsync())
            .Where(t => projectNames.ContainsKey(t.ProjectId))
            .ToDictionary(t => t.Id, t => projectNames[t.ProjectId], StringComparer.Ordinal);

        foreach (var group in (await _memberships.GetAllAsync()).GroupBy(m => m.EmployeeId, StringComparer.Ordinal))
        {
            var names = group
                .Select(m => projectByTeam.GetValueOrDefault(m.TeamId))
                .Where(n => n is not null)
                .Select(n => n!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (names.Count > 0) result[group.Key] = names;
        }

        return result;
    }

    public async Task<PayrollXeroSyncResult> SyncAsync(string runId)
    {
        var run = await _runs.GetByIdAsync(runId);
        if (run is null) return PayrollXeroSyncResult.NotFound();

        // Already posted. Not an error — pressing the button twice should read
        // as "nothing to do", not as a failure.
        if (!string.IsNullOrWhiteSpace(run.XeroManualJournalId))
        {
            return PayrollXeroSyncResult.Existing(run.XeroManualJournalId!);
        }

        // Posting a draft would put figures in the ledger that are still being
        // edited, and the run has no approval behind it.
        if (run.Status != PayrollRunStatus.SUBMITTED)
        {
            return PayrollXeroSyncResult.Refused(
                "Only an approved payroll run can post to Xero. Approve this run first.");
        }

        if (!await _xero.IsConnectedAsync())
        {
            return PayrollXeroSyncResult.Refused("This organization isn't connected to Xero.");
        }

        var loaded = await BuildInputAsync(runId);
        if (loaded is null) return PayrollXeroSyncResult.NotFound();

        var journal = BuildJournal(loaded.Value.Input, loaded.Value.Context);
        if (!journal.Ok)
        {
            await RecordFailureAsync(run, journal.Error!);
            return PayrollXeroSyncResult.Refused(journal.Error!);
        }

        var request = new XeroManualJournalRequest(
            journal.Narration,
            journal.Date,
            [.. journal.Lines.Select(l => new XeroManualJournalLine(
                l.Amount,
                l.Description,
                l.AccountCode,
                l.AllTags.Count == 0
                    ? null
                    : [.. l.AllTags.Select(t => new XeroTrackingRef(t.Category, t.Option))]))],
            IdempotencyKey(run.Id, journal));

        try
        {
            var posted = await _xero.CreateManualJournalAsync(request);

            run.XeroManualJournalId = posted.ManualJournalId;
            run.XeroJournalNumber = posted.Narration ?? journal.Narration;
            run.XeroSyncStatus = Claims.Entities.XeroSyncStatus.SYNCED;
            // A stale error sitting beside a posted journal reads as a problem
            // that is not there.
            run.XeroSyncError = null;
            run.XeroSyncedAt = DateTime.UtcNow;

            // The rules can change after this; the run keeps the ones it
            // posted with, so a month can always say why it split as it did.
            var mapping = loaded.Value.Input.Mapping;
            run.XeroSplitSnapshotJson = mapping.JournalMode == XeroJournalMode.AI_SPLIT
                ? JsonSerializer.Serialize(
                    new { mapping.SplitPrompt, mapping.SplitAnswers, mapping.SplitRules, PostedAt = run.XeroSyncedAt },
                    PayrollSnapshotJson.Options)
                : null;
            await _runs.UpdateAsync(run);

            await _audit.WriteAsync(new AuditEvent(
                AuditActions.PayrollRunXeroSync,
                $"Posted {PayrollPeriodLabel.ForRun(run)} payroll to Xero",
                TargetType: "PayrollRun",
                TargetId: run.Id,
                Metadata: new
                {
                    posted.ManualJournalId,
                    LineCount = journal.Lines.Count,
                    JournalMode = loaded.Value.Input.Mapping.JournalMode.ToString(),
                }));

            return PayrollXeroSyncResult.Posted(posted.ManualJournalId, journal.Lines.Count);
        }
        catch (XeroConnectionException ex)
        {
            await RecordFailureAsync(run, ex.Message);
            return PayrollXeroSyncResult.Refused(ex.Message);
        }
    }

    // Called from the approval. Best effort on purpose: an approval already
    // given must not be undone because Xero was unreachable, so every failure
    // is swallowed into the run's own error column and the audit trail.
    public async Task SyncOnApprovalAsync(string runId)
    {
        try
        {
            var settings = await _settings.GetEffectiveAsync();
            if (!settings.SyncPayrollToXeroOnSubmit) return;

            var result = await SyncAsync(runId);
            if (!result.Ok)
            {
                _log.LogWarning(
                    "Payroll run {RunId} approved but did not post to Xero: {Error}",
                    runId, result.Error);
            }
        }
        catch (Exception ex)
        {
            // Deliberately broad. Nothing this method can hit is worth failing
            // an approval over, and the run keeps its own error column.
            _log.LogError(ex, "Payroll run {RunId} approved; Xero post threw.", runId);
        }
    }

    private async Task RecordFailureAsync(Entities.PayrollRun run, string error)
    {
        run.XeroSyncStatus = Claims.Entities.XeroSyncStatus.ERROR;
        run.XeroSyncError = error;
        await _runs.UpdateAsync(run);

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.PayrollRunXeroSync,
            $"Failed to post {PayrollPeriodLabel.ForRun(run)} payroll to Xero",
            TargetType: "PayrollRun",
            TargetId: run.Id,
            Status: "FAILURE",
            ErrorReason: error));
    }

    // Derived from the run AND the payload. The run id alone would let a
    // corrected re-post silently return the first journal; including the
    // payload means an unchanged retry is idempotent while a genuinely
    // different journal is a new one.
    private static string IdempotencyKey(string runId, Journal journal)
    {
        var canonical = string.Join("|", journal.Lines.Select(l =>
            $"{l.AccountCode}:{l.Amount}:{l.Description}:"
            + string.Join(",", l.AllTags.Select(t => $"{t.Category}={t.Option}"))));

        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(canonical)))[..12].ToLowerInvariant();

        return $"payroll-run-{runId}-{hash}";
    }

    // ─── Loading ────────────────────────────────────────────────────────

    private async Task<(Entities.PayrollRun Run, PayrollJournal.Input Input, PayrollJournalSplit.Context Context)?>
        BuildInputAsync(string runId, bool withSplitContext = false)
    {
        var run = await _runs.GetByIdAsync(runId);
        if (run is null) return null;

        var settings = await _settings.GetEffectiveAsync();
        var mapping = ParseMapping(settings.XeroMappingJson);

        var payslips = await _payslips.GetForRunAsync(runId);
        var lineItems = (await _payslips.GetLineItemsForRunAsync(runId))
            .GroupBy(li => li.PayslipId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        // Xero addresses accounts by CODE; the mapping and the claims store
        // ids. One lookup serves both.
        var accounts = await _accounts.GetAllAsync();
        var codeByLocalId = accounts
            .Where(a => !string.IsNullOrWhiteSpace(a.Code))
            .ToDictionary(a => a.Id, a => a.Code, StringComparer.Ordinal);

        // The settings page saves the LOCAL chart-of-account id. Keying this
        // by XeroAccountId alone made every mapped slot read as unmapped, so
        // the sync refused with "No Xero account is mapped for Salary" while
        // the page showed one. Local ids resolve first; a Xero id is still
        // accepted so a blob written the other way keeps working.
        var accountCodeById = new Dictionary<string, string>(codeByLocalId, StringComparer.Ordinal);

        // A claim posts only to an account Xero has, as the claim bill does. A
        // local one (e.g. a starter claim item type) may share a code with a
        // different Xero account and post to the wrong ledger; leaving it out
        // refuses the claim as unmapped instead.
        var claimCodeByLocalId = accounts
            .Where(a => !string.IsNullOrWhiteSpace(a.Code) && !string.IsNullOrWhiteSpace(a.XeroAccountId))
            .ToDictionary(a => a.Id, a => a.Code, StringComparer.Ordinal);
        foreach (var a in accounts)
        {
            if (string.IsNullOrWhiteSpace(a.XeroAccountId) || string.IsNullOrWhiteSpace(a.Code)) continue;
            accountCodeById.TryAdd(a.XeroAccountId, a.Code);
        }

        // Archived covers inactive in Xero AND retired because the connected
        // org no longer has it. Keyed both ways, like the codes.
        var archivedAccounts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var a in accounts.Where(a => a.IsArchived))
        {
            var label = string.Join(" · ", new[] { a.Code, a.Name }.Where(p => !string.IsNullOrWhiteSpace(p)));
            archivedAccounts.TryAdd(a.Id, label);
            if (!string.IsNullOrWhiteSpace(a.XeroAccountId)) archivedAccounts.TryAdd(a.XeroAccountId, label);
        }

        // A claim's expense account, for the reimbursement debits.
        var claimAccounts = (await _claims.GetPayrollReimbursableAsync())
            .Where(c => !string.IsNullOrWhiteSpace(c.ChartOfAccountId))
            .ToDictionary(c => c.Id, c => c.ChartOfAccountId!, StringComparer.Ordinal);

        var (trackingName, trackingOptions) = await TrackingAsync(mapping);

        // Who is where is read live: a split by project or department follows
        // who is in which now, like the run's other live identifiers.
        var people = await PeopleAsync();

        var rows = payslips.Select(p =>
        {
            var items = lineItems.TryGetValue(p.Id, out var list) ? list : [];
            var person = people.GetValueOrDefault(p.EmployeeProfileId);

            return new PayrollJournal.EmployeeRow
            {
                EmployeeName = p.SnapshotName,
                EmployeeId = p.EmployeeProfileId,
                Department = person?.Department,
                Projects = person?.Projects ?? [],
                JobTitle = person?.JobTitle,
                EmploymentType = person?.EmploymentType,
                Role = person?.Role,
                Policy = person?.Policy,
                // No project dimension on a payslip yet — every line lands
                // under "(No project)" until one exists. Kept as a real value
                // rather than null so the tracking lookup has something to
                // match and the journal reads consistently.
                ProjectName = PayrollJournal.NoProject,
                ProratedPay = p.ProratedPay,
                OtPay = p.OtPay,
                NetPay = p.NetPay,
                EpfEmployee = p.EpfEmployee,
                EpfEmployer = p.EpfEmployer,
                SocsoEmployee = p.SocsoEmployee,
                SocsoEmployer = p.SocsoEmployer,
                EisEmployee = p.EisEmployee,
                EisEmployer = p.EisEmployer,
                SkbbkEmployee = p.SkbbkEmployee,
                Pcb = p.Pcb,
                PcbBorneByEmployer = p.PcbBorneByEmployer,
                Cp38 = p.Cp38,
                VoluntaryPcb = p.VoluntaryPcb,
                Zakat = p.Zakat,
                Hrdf = p.Hrdf,
                Allowances =
                [
                    .. items
                        .Where(li => li.Kind == PayslipLineKind.ALLOWANCE && li.Amount > 0m)
                        .Select(li => new PayrollJournal.JournalLineItem(li.Category, li.Amount, li.Label)),
                ],
                Deductions =
                [
                    .. items
                        .Where(li => li.Kind == PayslipLineKind.DEDUCTION && li.Amount > 0m)
                        .Select(li => new PayrollJournal.JournalLineItem(li.Category, li.Amount, li.Label)),
                ],
                Reimbursements =
                [
                    .. items
                        .Where(li => li.Kind == PayslipLineKind.REIMBURSEMENT
                            && li.Amount > 0m
                            && li.ClaimId is not null)
                        .Select(li => new PayrollJournal.JournalReimbursement(
                            li.ClaimId!,
                            li.Amount,
                            li.Label,
                            ClaimAccountCode(li.ClaimId!, claimAccounts, claimCodeByLocalId),
                            null)),
                ],
            };
        }).ToList();

        // Only the split reads it, and it costs a Xero round trip.
        var splitContext = withSplitContext || mapping.JournalMode == XeroJournalMode.AI_SPLIT
            ? await SplitContextAsync(payslips)
            : new PayrollJournalSplit.Context();

        return (run, new PayrollJournal.Input
        {
            PeriodYear = run.PeriodYear,
            PeriodMonth = run.PeriodMonth,
            RunName = run.Name,
            Mapping = mapping,
            AccountCodeById = accountCodeById,
            ArchivedAccounts = archivedAccounts,
            TrackingCategoryName = trackingName,
            TrackingOptions = trackingOptions,
            Rows = rows,
        }, splitContext);
    }

    private static string? ClaimAccountCode(
        string claimId,
        IReadOnlyDictionary<string, string> claimAccounts,
        IReadOnlyDictionary<string, string> codeByLocalId) =>
        claimAccounts.TryGetValue(claimId, out var accountId)
            && codeByLocalId.TryGetValue(accountId, out var code)
                ? code
                : null;

    public async Task<IReadOnlyList<PayrollXeroTrackingCategory>> GetTrackingCategoriesAsync()
    {
        if (!await _xero.IsConnectedAsync()) return [];

        try
        {
            return
            [
                .. (await _xero.GetTrackingCategoriesAsync()).Select(c =>
                    new PayrollXeroTrackingCategory(
                        c.TrackingCategoryId,
                        c.Name,
                        [.. c.Options.Select(o => o.Name)]))
            ];
        }
        catch (Exception)
        {
            // Deliberately broad. Beyond an unreachable Xero, the stored
            // refresh token can fail to DECRYPT — a data-protection key that
            // is no longer in the ring throws CryptographicException, not a
            // XeroConnectionException, and a narrow catch turned this into a
            // 500 that took the whole settings page down. The accounts are
            // stored locally and remain configurable either way, so an empty
            // list is the right answer to every failure here.
            return [];
        }
    }

    // The tracking category's live name and options. A category the admin
    // picked and has since deleted in Xero yields no tracking rather than a
    // rejected journal.
    private async Task<(string? Name, IReadOnlySet<string> Options)> TrackingAsync(
        PayrollXeroMapping mapping)
    {
        if (string.IsNullOrWhiteSpace(mapping.TrackingCategoryId)) return (null, new HashSet<string>());

        try
        {
            var categories = await _xero.GetTrackingCategoriesAsync();
            var category = categories.FirstOrDefault(c =>
                string.Equals(c.TrackingCategoryId, mapping.TrackingCategoryId, StringComparison.Ordinal));

            if (category is null) return (null, new HashSet<string>());

            return (category.Name,
                category.Options.Select(o => o.Name).ToHashSet(StringComparer.Ordinal));
        }
        catch (XeroConnectionException)
        {
            // Tracking is a reporting nicety. Losing it is far better than
            // failing the whole post over it.
            return (null, new HashSet<string>());
        }
    }

    // An unset or unreadable blob is "not configured yet", which the journal
    // builder then refuses with a message naming the missing accounts. That is
    // a better failure than a parse exception the admin cannot act on.
    private static PayrollXeroMapping ParseMapping(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new PayrollXeroMapping();

        try
        {
            return JsonSerializer.Deserialize<PayrollXeroMapping>(json, PayrollSnapshotJson.Options)
                ?? new PayrollXeroMapping();
        }
        catch (JsonException)
        {
            return new PayrollXeroMapping();
        }
    }
}
