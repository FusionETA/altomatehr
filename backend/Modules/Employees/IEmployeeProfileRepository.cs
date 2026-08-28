using AltomateHR.Api.Modules.Employees.Entities;

namespace AltomateHR.Api.Modules.Employees;

// Data access for the rich per-org employee profile. Tenant-scoped: the query
// filter restricts every read/write to the caller's active org.
public interface IEmployeeProfileRepository
{
    // The profile for a user IN THE CURRENT ORG (null if none saved yet).
    Task<EmployeeProfile?> GetByUserAsync(string userId);

    Task<EmployeeProfile> AddAsync(EmployeeProfile profile);
    Task UpdateAsync(EmployeeProfile profile);
}
