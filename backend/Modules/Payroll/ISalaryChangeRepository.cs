using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

public interface ISalaryChangeRepository
{
    // One employee's history, most recent change first.
    Task<List<SalaryChange>> GetForEmployeeAsync(string employeeProfileId);

    // Every change taking effect inside [from, to]. One query per run rather
    // than one per employee.
    Task<List<SalaryChange>> GetEffectiveInRangeAsync(DateTime from, DateTime to);

    Task<SalaryChange> AddAsync(SalaryChange change);
}
