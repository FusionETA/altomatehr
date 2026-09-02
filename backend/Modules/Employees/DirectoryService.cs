using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Employees.Entities;

namespace AltomateHR.Api.Modules.Employees;

// Thin by design — see IDirectoryService for why it exists. It holds no rules
// of its own; it is the seam that lets Employees and Auth change their storage
// without breaking every other module.
public class DirectoryService : IDirectoryService
{
    private readonly IOrganizationMembershipRepository _memberships;
    private readonly IUserRepository _users;

    public DirectoryService(IOrganizationMembershipRepository memberships, IUserRepository users)
    {
        _memberships = memberships;
        _users = users;
    }

    public Task<OrganizationMembership?> GetMembershipForUserAsync(string userId) =>
        _memberships.GetForUserInCurrentOrgAsync(userId);

    public Task<List<OrganizationMembership>> GetMembershipsForCurrentOrgAsync() =>
        _memberships.GetForCurrentOrgAsync();

    public Task<OrganizationMembership?> GetMembershipAsync(string organizationId, string userId) =>
        _memberships.GetAsync(organizationId, userId);

    public Task<List<OrganizationMembership>> GetMembershipsByUserAsync(string userId) =>
        _memberships.GetByUserAsync(userId);

    public Task<int> CountMembershipsByShiftAsync(string shiftId) =>
        _memberships.CountByShiftIdAsync(shiftId);

    public Task<User?> GetUserAsync(string id) => _users.GetByIdAsync(id);

    public Task<List<User>> GetUsersAsync() => _users.GetAllAsync();
}
