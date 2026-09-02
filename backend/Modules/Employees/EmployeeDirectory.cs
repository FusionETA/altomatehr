using AltomateHR.Api.Modules.Auth;

namespace AltomateHR.Api.Modules.Employees;

public class EmployeeDirectory : IEmployeeDirectory
{
    private readonly IOrganizationMembershipRepository _memberships;
    private readonly IUserRepository _users;

    public EmployeeDirectory(IOrganizationMembershipRepository memberships, IUserRepository users)
    {
        _memberships = memberships;
        _users = users;
    }

    public async Task<EmployeeDirectorySnapshot> GetSnapshotAsync()
    {
        // Memberships are tenant-filtered, so this can only ever see the caller's
        // own org — which is what stops an import from resolving an email to
        // somebody in a different tenant.
        var members = await _memberships.GetForCurrentOrgAsync();
        var usersById = (await _users.GetAllAsync()).ToDictionary(u => u.Id, StringComparer.Ordinal);

        var identities = members
            .Select(m => usersById.TryGetValue(m.UserId, out var user)
                ? new EmployeeIdentity(m.UserId, user.Email, user.Name, m.Role)
                : null)
            .Where(i => i is not null)
            .Select(i => i!);

        return new EmployeeDirectorySnapshot(identities);
    }
}
