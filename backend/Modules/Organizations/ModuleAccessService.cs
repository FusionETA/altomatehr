using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Employees;

namespace AltomateHR.Api.Modules.Organizations;

// Resolves the effective module set for whoever is calling right now, reading the org's
// package + the caller's per-admin grant. Used by [RequireModule]. One org read (+ one
// membership read for humans) per gated request; always fresh, so a plan change takes
// effect immediately.
public class ModuleAccessService : IModuleAccessService
{
    private readonly IDirectoryService _directory;
    private readonly IOrganizationRepository _orgs;
    private readonly ICurrentUser _currentUser;

    public ModuleAccessService(
        IOrganizationRepository orgs,
        IDirectoryService directory,
        ICurrentUser currentUser)
    {
        _orgs = orgs;
        _directory = directory;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyCollection<string>> GetEnabledModulesAsync()
    {
        var ceiling = await GetOrgModulesAsync();
        if (ceiling.Count == 0) return ceiling;

        // Admin grant only applies to a real member whose role IS Admin. A wp_live
        // key's synthetic userId ("apikey:...") has no membership → full ceiling
        // (keys are scope-gated separately). An Owner has full access. And a grant
        // left on someone later moved to Employee or Supervisor (the employee form
        // can set the column for any role) must not narrow them — they have no
        // admin surface for it to narrow, only their own claims and approvals.
        IReadOnlyCollection<string>? grant = null;
        var userId = _currentUser.UserId;
        if (userId is not null)
        {
            var membership = await _directory.GetMembershipForUserAsync(userId);
            if (membership?.Modules is not null
                && string.Equals(membership.Role, "Admin", StringComparison.OrdinalIgnoreCase))
                grant = OrgModules.Split(membership.Modules);
        }

        return OrgModules.Effective(ceiling, grant);
    }

    public async Task<IReadOnlyCollection<string>> GetOrgModulesAsync()
    {
        var orgId = _currentUser.OrganizationId;
        if (orgId is null) return Array.Empty<string>();

        var org = await _orgs.GetByIdAsync(orgId);
        if (org is null) return Array.Empty<string>();

        return OrgModules.DeriveOrgEnabledModules(
            org.Plan, org.Tier, OrgModules.Split(org.Addons));
    }
}
