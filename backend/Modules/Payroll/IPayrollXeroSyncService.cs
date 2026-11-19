using AltomateHR.Api.Modules.Claims.Entities;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

// Posting a payroll run to Xero as one manual journal.
public interface IPayrollXeroSyncService
{
    // The lines that would post, or why they cannot. Writes nothing.
    Task<PayrollXeroPreview?> PreviewAsync(string runId);

    // Post. Idempotent on the run: a second call after a success reports the
    // journal already there rather than creating another.
    Task<PayrollXeroSyncResult> SyncAsync(string runId);

    // Fired from the approval, and only when the org has opted in. Best
    // effort — an approval already given must never be undone because an
    // accounting integration was unreachable.
    Task SyncOnApprovalAsync(string runId);

    // The org's Xero tracking categories, for the settings picker. Empty
    // when Xero is not connected or is unreachable — configuring the rest of
    // the mapping should not be blocked by a dropdown that cannot load.
    Task<IReadOnlyList<PayrollXeroTrackingCategory>> GetTrackingCategoriesAsync();

    // The AI split tried on a real run (the given one, else the latest with
    // payslips): the lines it replaces, the lines it would post, and every
    // check. With no run yet, only the checks that need none.
    Task<PayrollXeroSplitPreview> PreviewSplitAsync(string? runId, IReadOnlyList<PayrollJournalSplitRule> rules);

    // What a split prompt may name: the org's active accounts, tracking
    // categories, departments and staff.
    Task<PayrollXeroSplitOrgContext> GetSplitOrgContextAsync();
}

public sealed record PayrollXeroSplitPreview(
    bool Ok,
    string? RunId,
    string? RunLabel,
    // The standard lines the split replaces.
    IReadOnlyList<PayrollXeroPreviewLine> Replaced,
    // The whole journal with the split applied.
    IReadOnlyList<PayrollXeroPreviewLine> Lines,
    IReadOnlyList<PayrollJournalSplit.Check> Checks,
    // Who went where: each person on the run, and which rule took each of
    // their amounts. Empty when there is no run to try the rules on.
    IReadOnlyList<PayrollXeroSplitAssignment> Assignments)
{
    public decimal ReplacedTotal => Replaced.Sum(l => l.Amount);
    public decimal TotalDebits => Lines.Where(l => l.Amount > 0m).Sum(l => l.Amount);
    public decimal TotalCredits => Lines.Where(l => l.Amount < 0m).Sum(l => -l.Amount);
}

public sealed record PayrollXeroSplitAccount(string Code, string Name, string Type);

public sealed record PayrollXeroSplitAssignment(
    string EmployeeName, IReadOnlyList<PayrollXeroSplitPlacement> Placements);

// `Source` and `Type` are display labels ("Allowances", "Commission"); the
// account is null when the amount went into the pool the fixed and
// percentage rules share.
public sealed record PayrollXeroSplitPlacement(
    string Source, string? Type, string Rule, string? AccountCode, decimal Amount);

public sealed record PayrollXeroSplitOrgContext(
    PayrollJournalSplit.Context Context,
    // The rules saved now, for prompts like "same as before, but…".
    IReadOnlyList<PayrollJournalSplitRule> SavedRules,
    // Split source → the account code the standard mapping posts it to.
    IReadOnlyDictionary<string, string?> StandardAccounts,
    IReadOnlyList<PayrollXeroSplitAccount> Accounts);

public sealed record PayrollXeroTrackingCategory(
    string TrackingCategoryId, string Name, IReadOnlyList<string> Options);

public sealed record PayrollXeroPreviewLine(
    string AccountCode, string Description, decimal Amount, string? TrackingOption,
    // The debit slot the line came from (null on credits) — lets the page
    // pick out the split lines.
    string? Source = null);

public sealed record PayrollXeroPreview(
    bool Ok,
    string? Error,
    string Narration,
    DateTime Date,
    IReadOnlyList<PayrollXeroPreviewLine> Lines,
    // Non-null once the run has posted — the page shows the journal rather
    // than offering the button again.
    string? XeroManualJournalId,
    XeroSyncStatus Status,
    // AI_SPLIT when the lines above are the split ones; then Checks says
    // what was checked, and why it cannot post when Ok is false.
    XeroJournalMode JournalMode = XeroJournalMode.STANDARD,
    IReadOnlyList<PayrollJournalSplit.Check>? Checks = null)
{
    // Shown beside the lines so an admin can see the journal balances before
    // committing, rather than discovering it from a Xero rejection.
    public decimal Balance => Lines.Sum(l => l.Amount);

    public decimal TotalDebits => Lines.Where(l => l.Amount > 0m).Sum(l => l.Amount);

    public decimal TotalCredits => Lines.Where(l => l.Amount < 0m).Sum(l => -l.Amount);
}

// Found=false is a 404. Ok=false with a message is a run that could not post
// and says why. AlreadyPosted reads as "nothing to do" rather than an error —
// the same vocabulary the claims sync uses.
public sealed record PayrollXeroSyncResult(
    bool Found,
    bool Ok,
    string? ManualJournalId,
    int LineCount,
    bool AlreadyPosted = false,
    string? Error = null)
{
    public static PayrollXeroSyncResult NotFound() => new(false, false, null, 0);

    public static PayrollXeroSyncResult Refused(string error) =>
        new(true, false, null, 0, false, error);

    public static PayrollXeroSyncResult Posted(string journalId, int lineCount) =>
        new(true, true, journalId, lineCount);

    public static PayrollXeroSyncResult Existing(string journalId) =>
        new(true, true, journalId, 0, AlreadyPosted: true);
}
