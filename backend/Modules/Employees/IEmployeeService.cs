using AltomateHR.Api.Modules.Employees.Dtos;
namespace AltomateHR.Api.Modules.Employees;

public interface IEmployeeService
{
    Task<IEnumerable<EmployeeDto>> GetAllAsync();
    Task<EmployeeSaveResult> CreateAsync(CreateEmployeeDto dto);
    Task<EmployeeSaveResult> UpdateAsync(string id, UpdateEmployeeDto dto);

    // Overwrite an employee's login password with one the admin types, for the
    // employee who can no longer reach the email the reset code goes to.
    Task<SetPasswordResult> SetPasswordAsync(string userId, string newPassword);
}

// Ok=false with Error → 400; Ok=false and Error null → the user wasn't found (404).
public record EmployeeSaveResult(bool Ok, EmployeeDto? Employee, string? Error);

// Same convention: Error null and Ok=false means "not found in this org".
public record SetPasswordResult(bool Ok, string? Error);
