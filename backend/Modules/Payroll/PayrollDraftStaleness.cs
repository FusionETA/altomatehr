using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Payroll;

// Tells open payroll drafts that something they were built from has changed.
//
// A generated draft freezes the inputs as they were: salary, statutory flags,
// loan instalments, unpaid-leave days, OT. Only adjustments and claim
// attachments used to mark a run stale, so a profile edit, a new loan or a
// deleted unpaid leave left the draft quietly wrong — its payslips still
// showing the old figures, and nothing stopping them being submitted. The
// previous system swept the drafts on each such change (markDraftsStaleForOrg,
// and its loan equivalent) and made the admin re-run; this is that sweep.
//
// Marking stale is what makes the submit guard refuse until payroll is re-run,
// and what puts the "run payroll again" banner on the run.
//
// Best-effort by design: the caller's own save has already succeeded, and a
// failure here must not turn it into an error. Logged, so a silent miss can be
// found.
public interface IPayrollDraftStaleness
{
    // Every generated draft in the current org. For inputs not tied to one
    // month — an employee's profile, payroll settings, a loan, YTD figures.
    Task MarkAllDraftsAsync();

    // Only drafts whose month overlaps [from, to], inclusive. For inputs that
    // belong to dates — unpaid leave, an overtime claim.
    Task MarkDraftsCoveringAsync(DateTime from, DateTime to);
}

public class PayrollDraftStaleness : IPayrollDraftStaleness
{
    private readonly IPayrollRunRepository _runs;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<PayrollDraftStaleness> _log;

    public PayrollDraftStaleness(
        IPayrollRunRepository runs, ICurrentUser currentUser, ILogger<PayrollDraftStaleness> log)
    {
        _runs = runs;
        _currentUser = currentUser;
        _log = log;
    }

    public Task MarkAllDraftsAsync() => MarkAsync(null);

    public Task MarkDraftsCoveringAsync(DateTime from, DateTime to) =>
        MarkAsync(MonthsBetween(from, to));

    // Every (year, month) from `from` to `to`, whichever order they arrive in.
    public static IReadOnlyCollection<(int Year, int Month)> MonthsBetween(DateTime from, DateTime to)
    {
        if (to < from) (from, to) = (to, from);
        var months = new List<(int, int)>();
        for (var d = new DateTime(from.Year, from.Month, 1); d <= to; d = d.AddMonths(1))
            months.Add((d.Year, d.Month));
        return months;
    }

    private async Task MarkAsync(IReadOnlyCollection<(int Year, int Month)>? periods)
    {
        // No org, no sweep: without one the tenant filter is off, and a
        // background job must not mark every company's drafts.
        var orgId = _currentUser.OrganizationId;
        if (string.IsNullOrEmpty(orgId)) return;

        try
        {
            await _runs.MarkDraftsMutatedAsync(orgId, periods);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Could not mark payroll drafts stale for org {OrganizationId}", orgId);
        }
    }
}
