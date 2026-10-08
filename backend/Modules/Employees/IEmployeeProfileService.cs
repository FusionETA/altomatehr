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

    // Every profile in the current org with a work-permit expiry on file, with
    // what decides whether it still matters (nationality, PR, archived, left).
    Task<IReadOnlyList<WorkPermitDto>> GetWorkPermitsAsync();

    // Fills in the fields of this DTO that are EMPTY on the employee's record,
    // and leaves every field that already has a value alone — the Documents
    // module's "Also save to employee record" for a gap found while writing a
    // letter. Returns the names of the fields written; null → not a member of
    // this org. (Default keeps hand-written test doubles compiling.)
    Task<IReadOnlyList<string>?> FillMissingFieldsAsync(string userId, FillEmployeeFieldsDto fields) =>
        Task.FromResult<IReadOnlyList<string>?>(null);
}
