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
        var orgId = _currentUser.OrganizationId;
        if (orgId is null) return Array.Empty<string>();

        var org = await _orgs.GetByIdAsync(orgId);
        if (org is null) return Array.Empty<string>();

        var ceiling = OrgModules.DeriveOrgEnabledModules(
            org.Plan, org.Tier, OrgModules.Split(org.Addons));

        // Admin grant only applies to a real member. A wp_live key's synthetic userId
        // ("apikey:...") has no membership → null grant → full ceiling (keys are scope-gated
        // separately). An Owner's grant is null → full ceiling too.
        IReadOnlyCollection<string>? grant = null;
        var userId = _currentUser.UserId;
        if (userId is not null)
        {
            var membership = await _directory.GetMembershipForUserAsync(userId);
            if (membership?.Modules is not null)
                grant = OrgModules.Split(membership.Modules);
        }

        return OrgModules.Effective(ceiling, grant);
    }
}
