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
}
