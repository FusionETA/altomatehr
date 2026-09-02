using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Employees.Entities;

namespace AltomateHR.Api.Modules.Employees;

// The read-only "who's who" front door.
//
// ADR-02 says a module must not reach into another module's repository, yet
// almost every module needs the same two lookups — "who is this user" and
// "what is their membership in this org". Before this existed, ten services
// injected IOrganizationMembershipRepository and four injected IUserRepository
// directly, which is how a documented one-off exception quietly became the rule.
//
// So this is the shared kernel, named: identity and membership READS that any
// module may depend on. Employees and Auth keep their repositories private and
// stay free to change how they store things.
//
// Reads only, deliberately. A module that needs to CREATE or CHANGE another
// module's data has a real domain reason to, and that belongs on the owning
// module's own service where the rules live — not behind a generic lookup.
public interface IDirectoryService
{
    // The caller's membership in the current org (the common case — the tenant
    // filter already scopes it). Null when the user isn't a member here.
    Task<OrganizationMembership?> GetMembershipForUserAsync(string userId);

    // Everyone in the current org.
    Task<List<OrganizationMembership>> GetMembershipsForCurrentOrgAsync();

    // A membership in an explicitly named org. Used by flows that run before a
    // tenant context exists (login, partner SSO), where the org can't be implied.
    Task<OrganizationMembership?> GetMembershipAsync(string organizationId, string userId);

    // Every org a user belongs to — the org-picker on login.
    Task<List<OrganizationMembership>> GetMembershipsByUserAsync(string userId);

    // How many people are assigned to a shift; the shift module guards deletes
    // with it.
    Task<int> CountMembershipsByShiftAsync(string shiftId);

    Task<User?> GetUserAsync(string id);

    Task<List<User>> GetUsersAsync();
}
