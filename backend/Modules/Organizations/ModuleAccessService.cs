using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Employees;

namespace AltomateHR.Api.Modules.Organizations;

// Resolves the effective module set for whoever is calling right now, reading the org's
// package + the caller's per-admin grant. Used by [RequireModule]. One org read (+ one
// membership read for humans) per gated request; always fresh, so a plan change takes
// effect immediately. Scoped, so both are read once per request and shared by every
// attribute and service that asks.
public class ModuleAccessService : IModuleAccessService
{
    private readonly IDirectoryService _directory;
    private readonly IOrganizationRepository _orgs;
    private readonly ICurrentUser _currentUser;

    private IReadOnlyCollection<string>? _ceiling;
    private AdminAccess? _access;

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

        var access = await GetAccessAsync();
        return OrgModules.Effective(ceiling, access.Grant?.Keys.ToList());
    }

    public async Task<ModuleLevel> GetModuleLevelAsync(string module)
    {
        var ceiling = await GetOrgModulesAsync();
        if (!ceiling.Contains(module, StringComparer.OrdinalIgnoreCase)) return ModuleLevel.None;
        return (await GetAccessAsync()).LevelFor(module);
    }

    public async Task<ModuleLevel> GetCallerLevelAsync(string module)
    {
        var level = await GetModuleLevelAsync(module);
        if (level == ModuleLevel.None || !_currentUser.IsScopedMachine) return level;

        var scopes = !_currentUser.HasScope($"{module}:read") ? ModuleLevel.None
            : _currentUser.HasScope($"{module}:write") ? ModuleLevel.Manage
            : ModuleLevel.View;
        return scopes < level ? scopes : level;
    }

    // Admin limits apply only to a real member whose role IS Admin. A wp_live
    // key's synthetic userId ("apikey:...") has no membership → full access
    // (keys are scope-gated separately). An Owner has full access. And limits
    // left on someone later moved to Employee or Supervisor (the employee form
    // can set the column for any role) must not narrow them — they have no
    // admin surface for it to narrow, only their own claims and approvals.
    //
    // Fusioneta support acts as an Admin without a membership: full access.
    public async Task<AdminAccess> GetAccessAsync()
    {
        if (_access is not null) return _access;

        var userId = _currentUser.UserId;
        var membership = userId is null ? null : await _directory.GetMembershipForUserAsync(userId);

        // A signed-in Admin with no membership here was REMOVED by the Owner
        // while their access token was still live (up to its lifetime). Give
        // them nothing rather than everything. API keys (synthetic "apikey:"
        // ids, role Admin, no membership) and Fusioneta support keep theirs.
        if (membership is null
            && userId is not null
            && !userId.StartsWith("apikey:", StringComparison.Ordinal)
            && !_currentUser.IsSupport
            && string.Equals(_currentUser.Role, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            _access = new AdminAccess(true, new Dictionary<string, ModuleLevel>(), false, []);
            return _access;
        }

        _access = membership is null
                  || !string.Equals(membership.Role, "Admin", StringComparison.OrdinalIgnoreCase)
            ? AdminAccess.Full
            : new AdminAccess(
                Restricted: true,
                Grant: membership.Modules is null ? null : OrgModules.ParseGrant(membership.Modules),
                CanChangeSettings: membership.CanChangeSettings,
                PolicyScope: membership.PolicyScope is null ? null : OrgModules.Split(membership.PolicyScope));

        return _access;
    }

    public async Task<IReadOnlyCollection<string>> GetOrgModulesAsync()
    {
        if (_ceiling is not null) return _ceiling;

        var orgId = _currentUser.OrganizationId;
        if (orgId is null) return Array.Empty<string>();

        var org = await _orgs.GetByIdAsync(orgId);
        if (org is null) return Array.Empty<string>();

        _ceiling = OrgModules.DeriveOrgEnabledModules(
            org.Plan, org.Tier, OrgModules.Split(org.Addons));
        return _ceiling;
    }
}
