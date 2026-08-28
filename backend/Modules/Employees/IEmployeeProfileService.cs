using AltomateHR.Api.Modules.Employees.Dtos;

namespace AltomateHR.Api.Modules.Employees;

public interface IEmployeeProfileService
{
    // The full profile for a member of the current org. Null → the user isn't a
    // member here (→ 404). If they're a member but have no profile saved yet,
    // returns a context-only shell (email/name + defaults) so the edit form has data.
    Task<EmployeeProfileDto?> GetAsync(string userId);

    // Upsert the profile. Null → not a member of this org (→ 404).
    Task<EmployeeProfileDto?> SaveAsync(string userId, EmployeeProfileDto dto);
}
