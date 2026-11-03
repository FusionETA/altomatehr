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

    // The gender on a current-org member's profile, or null when none is on
    // file — what leave eligibility (maternity / paternity) reads. (Default
    // keeps hand-written test doubles compiling.)
    Task<Entities.Gender?> GetGenderAsync(string userId) => Task.FromResult<Entities.Gender?>(null);

    // The same for every current-org member at once (userId → gender), for
    // org-wide grids; anyone without a gender on file is simply absent.
    Task<IReadOnlyDictionary<string, Entities.Gender>> GetGendersAsync() =>
        Task.FromResult<IReadOnlyDictionary<string, Entities.Gender>>(new Dictionary<string, Entities.Gender>());

    // The join date on a current-org member's profile, or null when none is on
    // file — what the Employment Act leave minimums (years of service) read.
    // (Default keeps hand-written test doubles compiling.)
    Task<DateTime?> GetJoinDateAsync(string userId) => Task.FromResult<DateTime?>(null);

    // Every profile's join date, with its org: scoped to the current org in a
    // request, and spanning all orgs when there is no tenant (the leave crons),
    // which is why the org travels with each row. No join date → absent.
    Task<IReadOnlyList<EmployeeJoinDate>> GetJoinDatesAsync() =>
        Task.FromResult<IReadOnlyList<EmployeeJoinDate>>([]);
}

public sealed record EmployeeJoinDate(string OrganizationId, string UserId, DateTime JoinDate);
