using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Payroll.Dtos;

namespace AltomateHR.Api.Modules.Payroll;

// The audit trail of what someone has been paid, and the mid-cycle
// corrections that trail implies.
public interface ISalaryChangeService
{
    Task<IReadOnlyList<SalaryChangeDto>> GetForEmployeeAsync(string employeeProfileId);

    // Record a change. Null when the salary did not actually move — a no-op
    // row would fill the history an IR dispute reads with noise.
    Task<SalaryChangeDto?> RecordAsync(
        EmployeeProfile before, EmployeeProfile after, RecordSalaryChangeDto dto);

    // What a generated run needs correcting for, and by how much. Advisory:
    // it computes the delta and suggests the line, the admin decides.
    Task<IReadOnlyList<SalaryChangeHints.Hint>> GetHintsForRunAsync(string runId);
}
