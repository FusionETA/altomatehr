using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Payroll.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Payroll;

public class PayrollRunRepository : IPayrollRunRepository
{
    private readonly AppDbContext _db;

    public PayrollRunRepository(AppDbContext db) => _db = db;

    // Newest period first — the runs list is read top-down and the current month
    // is the one an admin wants.
    public Task<List<PayrollRun>> GetAllAsync() =>
        _db.PayrollRuns
            .OrderByDescending(r => r.PeriodYear)
            .ThenByDescending(r => r.PeriodMonth)
            .ToListAsync();

    public Task<PayrollRun?> GetByIdAsync(string id) =>
        _db.PayrollRuns.FirstOrDefaultAsync(r => r.Id == id);

    public Task<PayrollRun?> GetByPeriodAsync(int year, int month) =>
        _db.PayrollRuns.FirstOrDefaultAsync(
            r => r.PeriodYear == year && r.PeriodMonth == month);

    public async Task<PayrollRun> AddAsync(PayrollRun run)
    {
        var now = DateTime.UtcNow;
        run.CreatedAt = now;
        run.UpdatedAt = now;
        _db.PayrollRuns.Add(run);   // StampTenant sets OrganizationId
        await _db.SaveChangesAsync();
        return run;
    }

    public async Task UpdateAsync(PayrollRun run)
    {
        run.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task UpdateManyAsync(IEnumerable<PayrollRun> runs)
    {
        var now = DateTime.UtcNow;
        foreach (var run in runs) run.UpdatedAt = now;

        await _db.SaveChangesAsync();
    }

    public Task<List<PayrollRun>> GetSubmittedLaterInYearAsync(int year, int afterMonth) =>
        _db.PayrollRuns
            .Where(r => r.PeriodYear == year
                     && r.PeriodMonth > afterMonth
                     && r.Status == PayrollRunStatus.SUBMITTED)
            .OrderBy(r => r.PeriodMonth)
            .ToListAsync();

    // Ordering (year, month) as a pair — a December run is earlier than the
    // following January, which a month-only comparison would miss.
    public Task<bool> HasEarlierSubmittedRunAsync(int year, int month) =>
        _db.PayrollRuns.AnyAsync(
            r => r.Status == PayrollRunStatus.SUBMITTED
              && (r.PeriodYear < year
                  || (r.PeriodYear == year && r.PeriodMonth < month)));

    public async Task DeleteAsync(PayrollRun run)
    {
        _db.PayrollRuns.Remove(run);
        await _db.SaveChangesAsync();
    }

    public async Task MarkMutatedAsync(string runId)
    {
        var run = await GetByIdAsync(runId);
        if (run is null) return;

        var now = DateTime.UtcNow;
        run.LastMutatedAt = now;
        run.UpdatedAt = now;
        await _db.SaveChangesAsync();
    }

    public async Task MarkDraftsMutatedAsync(
        string organizationId, IReadOnlyCollection<(int Year, int Month)>? periods)
    {
        // Ungenerated runs are skipped: with no payslips there is nothing to
        // be behind, and flagging one would show a stale banner on an empty run.
        //
        // A dated input (unpaid leave, overtime) also stamps the month's run
        // when it's AWAITING APPROVAL or APPROVED. Nothing shows for those —
        // IsStale only reads true on a DRAFT, so approval goes ahead untouched —
        // but if the run is later sent back or reverted to draft, the stamp is
        // what makes it say "re-run": its payslips predate the change. The
        // org-wide sweep (periods null) stays drafts-only; it fires on every
        // profile or settings edit and would otherwise touch every filed month.
        // Plain comparisons, not statuses.Contains(r.Status): against the
        // string-converted enum that version stamped nothing in the tests, and
        // the caller swallows errors by design — so it failed silently.
        var includeLocked = periods is not null;
        var runs = await _db.PayrollRuns
            .Where(r => r.OrganizationId == organizationId
                        && r.GeneratedAt != null
                        && (r.Status == PayrollRunStatus.DRAFT
                            || (includeLocked
                                && (r.Status == PayrollRunStatus.PENDING_APPROVAL
                                    || r.Status == PayrollRunStatus.SUBMITTED))))
            .ToListAsync();
        if (periods is not null)
            runs = runs.Where(r => periods.Contains((r.PeriodYear, r.PeriodMonth))).ToList();
        if (runs.Count == 0) return;

        var now = DateTime.UtcNow;
        foreach (var run in runs)
        {
            run.LastMutatedAt = now;
            // Only an editable run is "updated" by this. A locked or filed run's
            // own record didn't change; the stamp just waits for a revert.
            if (run.Status == PayrollRunStatus.DRAFT) run.UpdatedAt = now;
        }
        await _db.SaveChangesAsync();
    }
}
