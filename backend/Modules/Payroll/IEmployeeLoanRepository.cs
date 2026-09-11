using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

public interface IEmployeeLoanRepository
{
    Task<List<EmployeeLoan>> GetAllAsync(string? employeeProfileId = null);

    Task<EmployeeLoan?> GetByIdAsync(string id);

    // Every ACTIVE loan in the org, for a generation pass. One query for the
    // whole run rather than one per employee.
    Task<List<EmployeeLoan>> GetActiveAsync();

    Task<EmployeeLoan> AddAsync(EmployeeLoan loan);
    Task UpdateAsync(EmployeeLoan loan);
    Task DeleteAsync(EmployeeLoan loan);
}
