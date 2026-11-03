using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;

namespace AltomateHR.Api.Modules.Organizations;

public class AdminAccessResolver : IAdminAccessResolver
{
    private readonly IOrganizationRepository _orgs;
    private readonly IDirectoryService _directory;
    private readonly IEmployeeScopeResolver _scope;
    private readonly IOrganizationStatus? _status;

    public AdminAccessResolver(
        IOrganizationRepository orgs,
        IDirectoryService directory,
        IEmployeeScopeResolver scope,
        IOrganizationStatus? status = null)
    {
        _orgs = orgs;
        _directory = directory;
        _scope = scope;
        _status = status;
    }

    public async Task<AdminAccessSnapshot?> ResolveAsync(string organizationId, string userId)
    {
        var membership = await _directory.GetMembershipAsync(organizationId, userId);
        if (membership is null || !OrgRoles.IsAdministrative(membership.Role)) return null;
        if (_status is not null && await _status.IsDeactivatedAsync(organizationId)) return null;

        var ceiling = await CeilingAsync(organizationId);
        if (ceiling is null) return null;

        var access = AccessFor(membership);

        // Only the modules the caller actually has, at the level they have
        // them: the org's plan ceiling narrowed by this admin's grant.
        var modules = ceiling
            .Select(m => (Module: m, Level: access.LevelFor(m)))
            .Where(x => x.Level > ModuleLevel.None)
            .ToDictionary(x => x.Module, x => x.Level, StringComparer.OrdinalIgnoreCase);

        // Resolving WHICH employees costs three reads, so it is done only when
        // the answer isn't "all of them".
        var employees = access.HasFullEmployeeScope
            ? null
            : (await _scope.ResolveAsync(organizationId, userId, access.PolicyScope!)).People;

        return new AdminAccessSnapshot(
            membership.Role,
            access.HasFullEmployeeScope,
            CanRunPayroll(modules, access),
            access.CanChangeSettings,
            modules,
            employees);
    }

    public async Task<AdminAccessFlags> FlagsAsync(string organizationId, OrganizationMembership membership)
    {
        var access = AccessFor(membership);
        var ceiling = await CeilingAsync(organizationId) ?? [];
        var modules = ceiling.ToDictionary(m => m, access.LevelFor, StringComparer.OrdinalIgnoreCase);

        return new AdminAccessFlags(access.HasFullEmployeeScope, CanRunPayroll(modules, access));
    }

    // Only an Admin is ever limited — an Owner has everything.
    private static AdminAccess AccessFor(OrganizationMembership membership) =>
        string.Equals(membership.Role, OrgRoles.Admin, StringComparison.OrdinalIgnoreCase)
            ? AdminAccess.ForAdmin(membership.Modules, membership.CanChangeSettings, membership.PolicyScope)
            : AdminAccess.Full;

    // The Payroll module at Manage. An admin limited to some policies runs
    // payroll too, for their own people only: a run holding anyone else stays
    // view-only for them (see PayrollRunInScopeAttribute), so a caller like
    // ABPay must keep their runs to the employees this snapshot lists.
    private static bool CanRunPayroll(IReadOnlyDictionary<string, ModuleLevel> modules, AdminAccess access) =>
        modules.GetValueOrDefault(OrgModules.Payroll) == ModuleLevel.Manage;

    private async Task<IReadOnlyCollection<string>?> CeilingAsync(string organizationId)
    {
        var org = await _orgs.GetByIdAsync(organizationId);
        return org is null
            ? null
            : OrgModules.DeriveOrgEnabledModules(org.Plan, org.Tier, OrgModules.Split(org.Addons));
    }
}
