using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

// Data access for payroll runs. Tenant-scoped throughout — the global query
// filter is what makes "the January run" mean this org's January run.
public interface IPayrollRunRepository
{
    Task<List<PayrollRun>> GetAllAsync();

    Task<PayrollRun?> GetByIdAsync(string id);

    // The run for a period, if one exists. There can only ever be one.
    Task<PayrollRun?> GetByPeriodAsync(int year, int month);

    Task<PayrollRun> AddAsync(PayrollRun run);

    Task UpdateAsync(PayrollRun run);

    // Persist several runs together. The revert cascade touches a whole year's
    // tail at once, and saying so is better than relying on one UpdateAsync
    // call happening to flush its siblings through shared change tracking.
    Task UpdateManyAsync(IEnumerable<PayrollRun> runs);

    // Every SUBMITTED run LATER in the same year. Reverting a month
    // invalidates the YTD-cumulative figures (PCB, the SOCSO/EIS relief) of
    // every month after it, so this is both the revert cascade and the warning
    // shown before confirming one.
    Task<List<PayrollRun>> GetSubmittedLaterInYearAsync(int year, int afterMonth);

    // Whether the org has ANY submitted run before this period. Distinguishes
    // "this org's first ever run" from "a gap is being papered over" — see the
    // chronology guard in PayrollRunService.
    Task<bool> HasEarlierSubmittedRunAsync(int year, int month);

    // Removes the run row itself. The caller clears what hangs off it first.
    Task DeleteAsync(PayrollRun run);

    // Stamp the run as having inputs that the payslips have not seen yet.
    // Called on every adjustment save/clear and claim attach/detach; cleared by
    // generation. Touches only LastMutatedAt/UpdatedAt, so it cannot disturb
    // totals a caller happens to be holding.
    Task MarkMutatedAsync(string runId);
}
