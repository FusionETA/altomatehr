using AltomateHR.Api.Modules.Payroll.Dtos;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

// Staff loans and salary advances, repaid by deduction from payroll.
public interface IEmployeeLoanService
{
    Task<IReadOnlyList<EmployeeLoanDto>> GetAllAsync(string? employeeProfileId = null);

    Task<EmployeeLoanDto?> GetAsync(string id);

    Task<EmployeeLoanDto> CreateAsync(SaveEmployeeLoanDto dto);

    // Null when the loan does not exist. Throws PayrollLoanException when the
    // terms cannot be repaid, or when the loan has already started.
    Task<EmployeeLoanDto?> UpdateAsync(string id, SaveEmployeeLoanDto dto);

    Task<EmployeeLoanDto?> SetStatusAsync(string id, LoanStatus status);

    // False when it does not exist. A loan that has started repaying is
    // cancelled rather than deleted — deleting it would erase the explanation
    // for deductions already taken.
    Task<bool> DeleteAsync(string id);

    // What each employee owes this period. Keyed by employee profile id; one
    // query for the whole run.
    Task<IReadOnlyDictionary<string, decimal>> GetRepaymentsForPeriodAsync(int year, int month);
}
