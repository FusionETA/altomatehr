using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Policies;

namespace AltomateHR.Api.Modules.Employees;

public class EmployeeScopeResolver : IEmployeeScopeResolver
{
    private readonly IDirectoryService _directory;
    private readonly IPolicyService _policies;

    public EmployeeScopeResolver(IDirectoryService directory, IPolicyService policies)
    {
        _directory = directory;
        _policies = policies;
    }

    public async Task<ScopedEmployees> ResolveAsync(
        string organizationId, string adminUserId, IReadOnlyList<string> policyIds)
    {
        var allowed = policyIds.ToHashSet(StringComparer.Ordinal);
        var members = await _directory.GetMembershipsForOrgAsync(organizationId);

        // An employee's policy is their own, or the org's default when they
        // have none (the same resolution payroll uses), so "everyone on
        // Monthly Workers" includes people who are on it by default.
        var effective = await _policies.GetEffectivePoliciesForOrgAsync(organizationId);

        // Staff only: a scope covers the people on those policies, never the
        // company's admins or owner (who resolve to the default policy).
        var userIds = members
            .Where(m => OrgRoles.IsOnPayroll(m.Role))
            .Where(m => effective.TryGetValue(m.UserId, out var p) && allowed.Contains(p.Id))
            .Select(m => m.UserId)
            .Append(adminUserId)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var profileByUser = (await _directory.GetProfilesForOrgAsync(organizationId))
            .GroupBy(p => p.UserId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);

        return new ScopedEmployees(
            userIds.Select(id => new ScopedEmployee(id, profileByUser.GetValueOrDefault(id))).ToList());
    }
}
