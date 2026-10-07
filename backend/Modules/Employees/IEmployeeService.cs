using AltomateHR.Api.Modules.Employees.Dtos;
namespace AltomateHR.Api.Modules.Employees;

public interface IEmployeeService
{
    Task<IEnumerable<EmployeeDto>> GetAllAsync();
    Task<EmployeeSaveResult> CreateAsync(CreateEmployeeDto dto);

    // What a blank employee ID would be assigned — the next in this company's
    // pattern. Shown as the add form's placeholder.
    Task<string> NextEmployeeNumberAsync() => Task.FromResult(EmployeeNumbers.FirstNumber);   // default for test doubles
    Task<EmployeeSaveResult> UpdateAsync(string id, UpdateEmployeeDto dto);

    // Overwrite an employee's login password with one the admin types, for the
    // employee who can no longer reach the email the reset code goes to.
    Task<SetPasswordResult> SetPasswordAsync(string userId, string newPassword);
}

// Ok=false with Error → 400; Ok=false and Error null → the user wasn't found (404).
// WelcomeEmailSent is null when none was asked for. False means the person WAS
// created and the mail failed — the caller says "added, but the email didn't go
// out" rather than reporting a failure that didn't happen.
public record EmployeeSaveResult(
    bool Ok, EmployeeDto? Employee, string? Error, bool? WelcomeEmailSent = null);

// Same convention: Error null and Ok=false means "not found in this org".
public record SetPasswordResult(bool Ok, string? Error);
