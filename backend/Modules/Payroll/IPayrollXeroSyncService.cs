using AltomateHR.Api.Modules.Claims.Entities;

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
}

public sealed record PayrollXeroTrackingCategory(
    string TrackingCategoryId, string Name, IReadOnlyList<string> Options);

public sealed record PayrollXeroPreviewLine(
    string AccountCode, string Description, decimal Amount, string? TrackingOption);

public sealed record PayrollXeroPreview(
    bool Ok,
    string? Error,
    string Narration,
    DateTime Date,
    IReadOnlyList<PayrollXeroPreviewLine> Lines,
    // Non-null once the run has posted — the page shows the journal rather
    // than offering the button again.
    string? XeroManualJournalId,
    XeroSyncStatus Status)
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
