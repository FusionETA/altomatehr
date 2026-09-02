namespace AltomateHR.Api.Modules.Employees;

public class EmployeeDirectory : IEmployeeDirectory
{
    private readonly IDirectoryService _directory;

    // Goes through IDirectoryService rather than the two repositories directly.
    // IUserRepository is Auth's, and reaching for it from here is exactly the
    // foreign injection ADR-02's shared-kernel change removed 21 times over.
    public EmployeeDirectory(IDirectoryService directory) => _directory = directory;

    public async Task<EmployeeDirectorySnapshot> GetSnapshotAsync()
    {
        // Memberships are tenant-filtered, so this can only ever see the caller's
        // own org — which is what stops an import from resolving an email to
        // somebody in a different tenant.
        var members = await _directory.GetMembershipsForCurrentOrgAsync();
        var usersById = (await _directory.GetUsersAsync()).ToDictionary(u => u.Id, StringComparer.Ordinal);

        var identities = members
            .Select(m => usersById.TryGetValue(m.UserId, out var user)
                ? new EmployeeIdentity(m.UserId, user.Email, user.Name, m.Role)
                : null)
            .Where(i => i is not null)
            .Select(i => i!);

        return new EmployeeDirectorySnapshot(identities);
    }
}
