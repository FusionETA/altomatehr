using AltomateHR.Api.Modules.Employees.Entities;

namespace AltomateHR.Api.Modules.Employees;

// Data access for the rich per-org employee profile. Tenant-scoped: the query
// filter restricts every read/write to the caller's active org.
public interface IEmployeeProfileRepository
{
    // The profile for a user IN THE CURRENT ORG (null if none saved yet).
    Task<EmployeeProfile?> GetByUserAsync(string userId);

    // Every profile in the current org. Payroll's generate path needs the
    // whole roster at once rather than one lookup per head.
    Task<List<EmployeeProfile>> GetAllForCurrentOrgAsync();

    // Profiles whose leave date has already passed but which are still
    // active. Used by the daily archive sweep, which runs with no request
    // context — so this deliberately IGNORES the tenant filter and covers
    // every org in one pass.
    Task<List<EmployeeProfile>> GetUnarchivedPastLeaversAsync(DateTime before, int max);

    Task<EmployeeProfile> AddAsync(EmployeeProfile profile);
    Task UpdateAsync(EmployeeProfile profile);
}
