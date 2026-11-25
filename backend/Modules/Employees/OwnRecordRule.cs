using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Employees;

// "Is this the caller's own record?" — for an Employee & admin acting in the
// admin portal, the one person whose pay, leave and claims they may not
// change there. Asked by the services behind every admin write that names a
// person in a body, a file or a record (imports, loans, a leave or claim
// edited by id); OwnRecordGuard asks the same of route values.
//
// Only an Admin session who is also on the staff is ever refused. The Owner
// (a small company's owner on its own payroll has nobody else to set it),
// support, API keys, admins who are admin only, and everyone in their own
// employee portal pass.
public interface IOwnRecordRule
{
    Task<bool> IsCallersOwnAsync(string? userId, string? employeeProfileId = null);
}

public sealed class OwnRecordRule : IOwnRecordRule
{
    public const string Refusal =
        "You can't change your own employee record from the admin portal. Ask another admin or the owner.";

    private readonly ICurrentUser _currentUser;
    private readonly IDirectoryService _directory;
    // Worked out once per request: whether the rule applies at all, and the
    // caller's own profile ids here.
    private (bool Applies, HashSet<string> ProfileIds)? _me;

    public OwnRecordRule(ICurrentUser currentUser, IDirectoryService directory)
    {
        _currentUser = currentUser;
        _directory = directory;
    }

    public async Task<bool> IsCallersOwnAsync(string? userId, string? employeeProfileId = null)
    {
        if (string.IsNullOrEmpty(userId) && string.IsNullOrEmpty(employeeProfileId)) return false;
        var (applies, profileIds) = await MeAsync();
        if (!applies) return false;
        return string.Equals(userId, _currentUser.UserId, StringComparison.Ordinal)
               || (employeeProfileId is not null && profileIds.Contains(employeeProfileId));
    }

    private async Task<(bool Applies, HashSet<string> ProfileIds)> MeAsync()
    {
        if (_me is { } cached) return cached;

        var me = _currentUser.UserId;
        var applies = me is not null
                      && _currentUser.OrganizationId is not null
                      && !_currentUser.IsSupport
                      && string.Equals(_currentUser.Role, OrgRoles.Admin, StringComparison.OrdinalIgnoreCase)
                      && (await _directory.GetMembershipForUserAsync(me))?.IsEmployed() == true;
        var profiles = applies
            ? (await _directory.GetProfilesByUserAsync(me!))
                .Where(p => p.OrganizationId == _currentUser.OrganizationId)
                .Select(p => p.Id)
                .ToHashSet(StringComparer.Ordinal)
            : [];

        _me = (applies, profiles);
        return _me.Value;
    }
}
