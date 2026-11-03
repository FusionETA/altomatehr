using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

public interface IUnpaidLeaveSettlementRepository
{
    // This year's FINAL settlements — the ones an approved run made.
    Task<List<UnpaidLeaveSettlement>> GetFinalForYearAsync(int year);

    // Wipe the run's settlements and write these instead (empty clears them).
    Task ReplaceForRunAsync(string payrollRunId, IReadOnlyList<UnpaidLeaveSettlement> settlements);

    // Approval makes a run's settlements final; a revert makes them provisional.
    Task SetFinalForRunsAsync(IReadOnlyCollection<string> payrollRunIds, bool final);
}
