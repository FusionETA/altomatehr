using System.Text.Json;
using AltomateHR.Api.Modules.Accounts;
using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Claims;
using AltomateHR.Api.Modules.Payroll.Entities;
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
    private readonly ILogger<PayrollXeroSyncService> _log;

    public PayrollXeroSyncService(
        IPayrollRunRepository runs,
        IPayslipRepository payslips,
        IPayrollSettingsService settings,
        IChartOfAccountRepository accounts,
        IClaimsService claims,
        IXeroService xero,
        IAuditService audit,
        ILogger<PayrollXeroSyncService> log)
    {
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

        var result = PayrollJournal.Build(input.Value.Input);

        return new PayrollXeroPreview(
            result.Ok,
            result.Error,
            result.Narration,
            result.Date,
            [.. result.Lines.Select(l => new PayrollXeroPreviewLine(
                l.AccountCode, l.Description, l.Amount, l.TrackingOption))],
            input.Value.Run.XeroManualJournalId,
            input.Value.Run.XeroSyncStatus);
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

        var journal = PayrollJournal.Build(loaded.Value.Input);
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
                l.TrackingCategoryName is null || l.TrackingOption is null
                    ? null
                    : [new XeroTrackingRef(l.TrackingCategoryName, l.TrackingOption)]))],
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
            await _runs.UpdateAsync(run);

            await _audit.WriteAsync(new AuditEvent(
                AuditActions.PayrollRunXeroSync,
                $"Posted {PayrollPeriodLabel.For(run.PeriodYear, run.PeriodMonth)} payroll to Xero",
                TargetType: "PayrollRun",
                TargetId: run.Id,
                Metadata: new { posted.ManualJournalId, LineCount = journal.Lines.Count }));

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
            $"Failed to post {PayrollPeriodLabel.For(run.PeriodYear, run.PeriodMonth)} payroll to Xero",
            TargetType: "PayrollRun",
            TargetId: run.Id,
            Status: "FAILURE",
            ErrorReason: error));
    }

    // Derived from the run AND the payload. The run id alone would let a
    // corrected re-post silently return the first journal; including the
    // payload means an unchanged retry is idempotent while a genuinely
    // different journal is a new one.
    private static string IdempotencyKey(string runId, PayrollJournal.Result journal)
    {
        var canonical = string.Join("|", journal.Lines.Select(l =>
            $"{l.AccountCode}:{l.Amount}:{l.Description}:{l.TrackingOption}"));

        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(canonical)))[..12].ToLowerInvariant();

        return $"payroll-run-{runId}-{hash}";
    }

    // ─── Loading ────────────────────────────────────────────────────────

    private async Task<(Entities.PayrollRun Run, PayrollJournal.Input Input)?> BuildInputAsync(
        string runId)
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
        var codeByXeroId = accounts
            .Where(a => !string.IsNullOrWhiteSpace(a.XeroAccountId) && !string.IsNullOrWhiteSpace(a.Code))
            .GroupBy(a => a.XeroAccountId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Code, StringComparer.Ordinal);
        var codeByLocalId = accounts
            .Where(a => !string.IsNullOrWhiteSpace(a.Code))
            .ToDictionary(a => a.Id, a => a.Code, StringComparer.Ordinal);

        // A claim's expense account, for the reimbursement debits.
        var claimAccounts = (await _claims.GetPayrollReimbursableAsync())
            .Where(c => !string.IsNullOrWhiteSpace(c.ChartOfAccountId))
            .ToDictionary(c => c.Id, c => c.ChartOfAccountId!, StringComparer.Ordinal);

        var (trackingName, trackingOptions) = await TrackingAsync(mapping);

        var rows = payslips.Select(p =>
        {
            var items = lineItems.TryGetValue(p.Id, out var list) ? list : [];

            return new PayrollJournal.EmployeeRow
            {
                EmployeeName = p.SnapshotName,
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
                Cp38 = p.Cp38,
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
                            ClaimAccountCode(li.ClaimId!, claimAccounts, codeByLocalId),
                            null)),
                ],
            };
        }).ToList();

        return (run, new PayrollJournal.Input
        {
            PeriodYear = run.PeriodYear,
            PeriodMonth = run.PeriodMonth,
            Mapping = mapping,
            AccountCodeById = codeByXeroId,
            TrackingCategoryName = trackingName,
            TrackingOptions = trackingOptions,
            Rows = rows,
        });
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
