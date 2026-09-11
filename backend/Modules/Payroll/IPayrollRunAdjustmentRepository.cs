using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

// Data access for the per-(run, employee) adjustment rows. Tenant-scoped
// throughout — the global query filter is what keeps one org's typed-in
// overtime out of another's run.
public interface IPayrollRunAdjustmentRepository
{
    // Every adjustment on a run. Generation reads the whole set at once rather
    // than one row per employee.
    Task<List<PayrollRunAdjustment>> GetForRunAsync(string payrollRunId);

    Task<PayrollRunAdjustment?> GetAsync(string payrollRunId, string employeeProfileId);

    // Create or update the row for (run, employee). The caller supplies the
    // already-patched entity; this only decides insert vs update.
    Task<PayrollRunAdjustment> UpsertAsync(PayrollRunAdjustment adjustment);

    // Remove the row entirely. Clearing an adjustment means having no row, not
    // having a row of zeroes — so "never touched" and "cleared" stay the same
    // state and generation has one case to handle, not two.
    Task<bool> DeleteAsync(string payrollRunId, string employeeProfileId);

    // Every adjustment on a run, for deleting the run itself.
    Task DeleteForRunAsync(string payrollRunId);
}
