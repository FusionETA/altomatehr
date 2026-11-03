using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

// The chosen roster for a run — see PayrollRunMember.
public interface IPayrollRunMemberRepository
{
    Task<List<PayrollRunMember>> GetForRunAsync(string payrollRunId);

    // The whole roster, whatever the caller's employee scope (still only
    // within the run's company) — see IPayslipRepository.GetProfileIdsForRunUnscopedAsync.
    Task<List<string>> GetProfileIdsForRunUnscopedAsync(PayrollRun run);

    // Replace the whole selection in one shot — used when a draft is created.
    Task ReplaceForRunAsync(PayrollRun run, IEnumerable<string> employeeProfileIds);
}
