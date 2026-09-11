using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

// Data access for claims attached to payroll runs.
public interface IPayrollRunClaimRepository
{
    Task<List<PayrollRunClaim>> GetForRunAsync(string payrollRunId);

    // Every attachment in the org, whichever run it sits on. The attachable
    // list needs it to mark a claim as already spoken for, and detach needs it
    // to find which run holds a claim.
    Task<List<PayrollRunClaim>> GetAllAsync();

    Task<PayrollRunClaim?> GetByClaimIdAsync(string claimId);

    Task<PayrollRunClaim> AddAsync(PayrollRunClaim attachment);

    Task<bool> DeleteByClaimIdAsync(string claimId);

    // Every attachment on a run, for deleting the run itself. The claims
    // themselves are untouched — they simply become free to attach again.
    Task DeleteForRunAsync(string payrollRunId);
}
