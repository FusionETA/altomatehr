using AltomateHR.Api.Modules.Employees.Entities;

namespace AltomateHR.Api.Modules.Employees;

public interface IOrganizationMembershipRepository
{
    // Cross-org: every org this user belongs to (bypasses the tenant filter —
    // we're keying on the user, not the active org, e.g. for the org switcher).
    Task<List<OrganizationMembership>> GetByUserAsync(string userId);

    // A specific (org, user) membership (bypasses the tenant filter — looked up explicitly).
    Task<OrganizationMembership?> GetAsync(string organizationId, string userId);

    // Memberships in the CURRENT (active) org — tenant-filtered.
    Task<List<OrganizationMembership>> GetForCurrentOrgAsync();

    // One user's membership in the CURRENT (active) org — tenant-filtered.
    Task<OrganizationMembership?> GetForUserInCurrentOrgAsync(string userId);

    // Current-org memberships whose SupervisorId == supervisorId.
    Task<List<OrganizationMembership>> GetBySupervisorAsync(string supervisorId);

    Task AddAsync(OrganizationMembership membership);
    Task UpdateAsync(OrganizationMembership membership);
}
