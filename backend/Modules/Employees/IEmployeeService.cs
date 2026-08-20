using AltomateHR.Api.Modules.Employees.Dtos;
namespace AltomateHR.Api.Modules.Employees;

public interface IEmployeeService
{
    Task<IEnumerable<EmployeeDto>> GetAllAsync();
    Task<EmployeeSaveResult> CreateAsync(CreateEmployeeDto dto);
    Task<EmployeeSaveResult> UpdateAsync(string id, UpdateEmployeeDto dto);
}

// Ok=false with Error → 400; Ok=false and Error null → the user wasn't found (404).
public record EmployeeSaveResult(bool Ok, EmployeeDto? Employee, string? Error);
