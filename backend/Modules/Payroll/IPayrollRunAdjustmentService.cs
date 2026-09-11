using AltomateHR.Api.Modules.Payroll.Dtos;

namespace AltomateHR.Api.Modules.Payroll;

// The per-(run, employee) adjustments an admin types: overtime hours, one-off
// allowances and deductions, and per-run tweaks to the profile's recurring
// rows.
//
// Every mutation here marks the run stale. The payslips on screen were built
// from the inputs as they were, so changing an input without saying so would
// leave an admin looking at figures that quietly no longer follow from them.
public interface IPayrollRunAdjustmentService
{
    Task<List<PayrollRunAdjustmentDto>> GetForRunAsync(string runId);

    Task<PayrollRunAdjustmentDto?> GetAsync(string runId, string employeeProfileId);

    // Everything the editor needs for one employee in one read: the saved row,
    // the profile's recurring allowances it can override, what attendance
    // derived, whether typed overtime will be paid at all, and the loan
    // installments generation will take anyway.
    Task<PayrollAdjustmentContextDto?> GetContextAsync(string runId, string employeeProfileId);

    // Replaces the row wholesale. Only a DRAFT run accepts one.
    Task<PayrollRunAdjustmentSaveResult> SaveAsync(
        string runId, string employeeProfileId, SavePayrollRunAdjustmentDto dto);

    // Deletes the row outright — see the repository for why "cleared" is an
    // absent row rather than a row of zeroes.
    Task<PayrollRunAdjustmentSaveResult> ClearAsync(string runId, string employeeProfileId);
}
