using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Policies.Dtos;
using AltomateHR.Api.Modules.Policies.Entities;

namespace AltomateHR.Api.Modules.Policies;

// Owns policy CRUD and resolves an employee's effective policy for other
// modules. Reads the employee's PolicyId from their active-org membership
// (a deliberate cross-module read — policy resolution is a Policy concern).
public class PolicyService : IPolicyService
{
    private readonly IDirectoryService _directory;
    private readonly IEmployeePolicyRepository _policies;
    private readonly IPolicyLeaveEntitlementRepository _entitlements;

    // Optional so hand-built instances in tests need not supply it; without
    // it every caller is treated as unrestricted (as an Owner is).
    private readonly Organizations.IModuleAccessService? _access;

    public PolicyService(
        IEmployeePolicyRepository policies,
        IPolicyLeaveEntitlementRepository entitlements,
        IDirectoryService directory,
        Organizations.IModuleAccessService? access = null)
    {
        _access = access;
        _policies = policies;
        _entitlements = entitlements;
        _directory = directory;
    }

    public async Task<IEnumerable<PolicyDto>> GetAllAsync()
    {
        var policies = await _policies.GetAllAsync();
        var result = new List<PolicyDto>(policies.Count);
        foreach (var policy in policies)
            result.Add(ToDto(policy, await _entitlements.GetByPolicyAsync(policy.Id)));
        return result;
    }

    public async Task<IEnumerable<PolicyDto>> GetVisibleAsync()
    {
        var access = await AccessAsync();
        var all = await GetAllAsync();
        if (access?.PolicyScope is null) return all;

        var visible = all.Where(p => access.CanEditPolicy(p.Id)).ToList();
        foreach (var policy in visible) policy.CanEdit = !await CoversAdministratorsAsync(policy.Id);
        return visible;
    }

    // Whether any of the company's admins or its owner resolves to this policy.
    private async Task<bool> CoversAdministratorsAsync(string policyId)
    {
        var administrators = (await _directory.GetMembershipsForCurrentOrgAsync())
            .Where(m => !Common.OrgRoles.IsOnPayroll(m.Role))
            .Select(m => m.UserId);
        return (await GetEffectivePoliciesForEmployeesAsync(administrators))
            .Values.Any(p => p.Id == policyId);
    }

    private async Task<Organizations.AdminAccess?> AccessAsync() =>
        _access is null ? null : await _access.GetAccessAsync();

    // Creating, archiving and choosing the default reach the whole company —
    // the Owner's call. Admins edit the policies they're given instead.
    private async Task<PolicySaveResult?> RefuseUnlessCompanyWideAsync(string what) =>
        await AccessAsync() is { ManagesAllPolicies: false }
            ? new PolicySaveResult(false, null,
                $"Only the organization's owner can {what}. You can edit the policies your admin access covers.",
                Forbidden: true)
            : null;

    public async Task<PolicySaveResult> CreateAsync(SavePolicyDto dto)
    {
        if (await RefuseUnlessCompanyWideAsync("create a policy") is { } refused) return refused;

        var name = dto.Name.Trim();
        if (await _policies.GetByNameAsync(name) is not null)
            return new PolicySaveResult(false, null, $"A policy named \"{name}\" already exists.");

        var now = DateTime.UtcNow;
        var policy = Apply(new EmployeePolicy { CreatedAt = now }, dto, name);
        policy.UpdatedAt = now;
        // First policy in the org becomes the default automatically.
        policy.IsDefault = await _policies.GetDefaultAsync() is null;

        await _policies.AddAsync(policy);
        await SaveEntitlementsAsync(policy.Id, dto.LeaveEntitlements);

        return new PolicySaveResult(true, ToDto(policy, ToEntities(policy.Id, dto.LeaveEntitlements)), null);
    }

    public async Task<PolicySaveResult> UpdateAsync(string id, SavePolicyDto dto)
    {
        var policy = await _policies.GetByIdAsync(id);
        var access = await AccessAsync();
        // A policy outside an admin's scope is one they can't see — a 404,
        // not a 403 that would confirm it exists.
        if (policy is null || access is not null && !access.CanEditPolicy(id))
            return new PolicySaveResult(false, null, null);   // → 404

        // An admin limited to some policies covers only the STAFF on them.
        // The company's admins and owner sit on a policy too — explicitly, or
        // through the default — and a limited admin mustn't change their leave
        // or clock-in rules, so such a policy stays the Owner's.
        if (access?.PolicyScope is not null && await CoversAdministratorsAsync(id))
        {
            return new PolicySaveResult(false, null,
                "This policy also applies to the company's admins or owner, so only the owner can change it.",
                Forbidden: true);
        }

        var name = dto.Name.Trim();
        var clash = await _policies.GetByNameAsync(name);
        if (clash is not null && clash.Id != id)
        {
            // Don't name a policy the caller can't see.
            return new PolicySaveResult(false, null, access is null || access.CanEditPolicy(clash.Id)
                ? $"A policy named \"{name}\" already exists."
                : $"The name \"{name}\" isn't available. Choose another.");
        }

        Apply(policy, dto, name);
        policy.UpdatedAt = DateTime.UtcNow;
        await _policies.UpdateAsync(policy);
        await SaveEntitlementsAsync(policy.Id, dto.LeaveEntitlements);

        return new PolicySaveResult(true, ToDto(policy, ToEntities(policy.Id, dto.LeaveEntitlements)), null);
    }

    public async Task<PolicySaveResult> SetArchivedAsync(string id, bool archived)
    {
        if (await RefuseUnlessCompanyWideAsync(archived ? "archive a policy" : "restore a policy") is { } refused)
            return refused;

        var policy = await _policies.GetByIdAsync(id);
        if (policy is null) return new PolicySaveResult(false, null, null);

        policy.IsArchived = archived;
        if (archived) policy.IsDefault = false;   // an archived policy can't be the default
        policy.UpdatedAt = DateTime.UtcNow;
        await _policies.UpdateAsync(policy);
        return new PolicySaveResult(true, ToDto(policy, await _entitlements.GetByPolicyAsync(policy.Id)), null);
    }

    public async Task<PolicySaveResult> SetDefaultAsync(string id)
    {
        if (await RefuseUnlessCompanyWideAsync("choose the default policy") is { } refused) return refused;

        var policy = await _policies.GetByIdAsync(id);
        if (policy is null || policy.IsArchived) return new PolicySaveResult(false, null, null);

        policy.IsDefault = true;
        policy.UpdatedAt = DateTime.UtcNow;
        await _policies.UpdateAsync(policy);
        await _policies.ClearDefaultExceptAsync(policy.Id);   // exactly one default
        return new PolicySaveResult(true, ToDto(policy, await _entitlements.GetByPolicyAsync(policy.Id)), null);
    }

    public async Task<PolicyModuleAccess> GetModuleAccessAsync(string employeeId)
    {
        var policy = await GetEffectivePolicyAsync(employeeId);

        // No policy resolved — an org that never set one up, or an employee not
        // assigned one. Full access rather than none: locking someone out of
        // the whole app because config is missing is the worse failure.
        if (policy is null) return PolicyModuleAccess.All;

        return new PolicyModuleAccess(
            policy.CanAccessAttendance, policy.CanAccessClaims, policy.CanAccessLeave);
    }

    public async Task<EmployeePolicy?> GetEffectivePolicyAsync(string employeeId)
    {
        var membership = await _directory.GetMembershipForUserAsync(employeeId);
        var policy = membership?.PolicyId is not null ? await _policies.GetByIdAsync(membership.PolicyId) : null;
        return policy ?? await _policies.GetDefaultAsync();
    }

    public async Task<IReadOnlyDictionary<string, EmployeePolicy>>
        GetEffectivePoliciesForEmployeesAsync(IEnumerable<string> employeeIds)
    {
        var ids = employeeIds.Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0) return new Dictionary<string, EmployeePolicy>(StringComparer.Ordinal);

        var memberships = await _directory.GetMembershipsForCurrentOrgAsync();
        var policyByUser = memberships.ToDictionary(m => m.UserId, m => m.PolicyId, StringComparer.Ordinal);

        // The org's policies are a handful of rows, so one read beats a lookup
        // per distinct id and keeps the default resolution in the same pass.
        var all = await _policies.GetAllAsync();
        var byId = all.ToDictionary(p => p.Id, StringComparer.Ordinal);
        // Mirrors GetDefaultAsync: an archived policy is never the fallback,
        // though an explicitly ASSIGNED one still resolves (same as
        // GetEffectivePolicyAsync, which looks the assignment up by id).
        var fallback = all.FirstOrDefault(p => p.IsDefault && !p.IsArchived);

        var result = new Dictionary<string, EmployeePolicy>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            var assigned = policyByUser.GetValueOrDefault(id);
            var policy = assigned is not null ? byId.GetValueOrDefault(assigned) : null;
            policy ??= fallback;
            if (policy is not null) result[id] = policy;
        }

        return result;
    }

    public async Task<bool> RequiresGeofenceAsync(string employeeId) =>
        (await GetEffectivePolicyAsync(employeeId))?.RequireGeofence ?? true;

    public async Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>>>
        GetLeaveEntitlementsForEmployeesAsync(IEnumerable<string> employeeIds)
    {
        var ids = employeeIds.Distinct().ToList();
        var memberships = await _directory.GetMembershipsForCurrentOrgAsync();
        var policyByUser = memberships.ToDictionary(m => m.UserId, m => m.PolicyId);
        var fallback = await _policies.GetDefaultAsync();

        // Load each DISTINCT policy's entitlements once.
        var neededPolicyIds = ids
            .Select(id => policyByUser.GetValueOrDefault(id) ?? fallback?.Id)
            .Where(pid => pid is not null)
            .Distinct()
            .ToList();

        var byPolicy = new Dictionary<string, IReadOnlyDictionary<string, double>>();
        foreach (var pid in neededPolicyIds)
        {
            var rows = await _entitlements.GetByPolicyAsync(pid!);
            byPolicy[pid!] = rows.ToDictionary(e => e.LeaveTypeId, e => e.DefaultDays);
        }

        var empty = new Dictionary<string, double>();
        return ids.ToDictionary(
            id => id,
            id =>
            {
                var pid = policyByUser.GetValueOrDefault(id) ?? fallback?.Id;
                return pid is not null && byPolicy.TryGetValue(pid, out var e)
                    ? e
                    : (IReadOnlyDictionary<string, double>)empty;
            });
    }

    public async Task<IReadOnlyDictionary<string, double>> GetLeaveEntitlementsAsync(string employeeId)
    {
        var policy = await GetEffectivePolicyAsync(employeeId);
        if (policy is null) return new Dictionary<string, double>();
        var entitlements = await _entitlements.GetByPolicyAsync(policy.Id);
        return entitlements.ToDictionary(e => e.LeaveTypeId, e => e.DefaultDays);
    }

    private async Task SaveEntitlementsAsync(string policyId, IEnumerable<PolicyLeaveEntitlementDto> dtos) =>
        await _entitlements.ReplaceForPolicyAsync(policyId, ToEntities(policyId, dtos));

    private static List<PolicyLeaveEntitlement> ToEntities(string policyId, IEnumerable<PolicyLeaveEntitlementDto> dtos)
    {
        var now = DateTime.UtcNow;
        return dtos
            .Where(e => !string.IsNullOrEmpty(e.LeaveTypeId))
            .Select(e => new PolicyLeaveEntitlement
            {
                PolicyId = policyId,
                LeaveTypeId = e.LeaveTypeId,
                DefaultDays = e.DefaultDays,
                CreatedAt = now,
                UpdatedAt = now,
            })
            .ToList();
    }

    private static EmployeePolicy Apply(EmployeePolicy policy, SavePolicyDto dto, string name)
    {
        policy.Name = name;
        policy.Description = dto.Description;
        policy.CanAccessAttendance = dto.CanAccessAttendance;
        policy.CanAccessClaims = dto.CanAccessClaims;
        policy.CanAccessLeave = dto.CanAccessLeave;
        policy.RequireGeofence = dto.RequireGeofence;
        policy.RequireSelfie = dto.RequireSelfie;
        policy.RequireClockOutSelfie = dto.RequireClockOutSelfie;
        policy.CaptureLocationOnBreakStart = dto.CaptureLocationOnBreakStart;
        policy.CaptureLocationOnBreakEnd = dto.CaptureLocationOnBreakEnd;
        policy.RequireIpWhitelist = dto.RequireIpWhitelist;
        policy.GeolocationEnabled = dto.GeolocationEnabled;
        policy.CaptureLocationOnClockIn = dto.CaptureLocationOnClockIn;
        policy.CaptureLocationOnClockOut = dto.CaptureLocationOnClockOut;
        policy.AutoClockOutEnabled = dto.AutoClockOutEnabled;
        policy.AutoClockOutAfterMinutes = dto.AutoClockOutAfterMinutes;
        policy.SalaryType = dto.SalaryType;
        policy.OtEnabled = dto.OtEnabled;
        policy.OtDailyThresholdMinutes = dto.OtDailyThresholdMinutes;
        policy.OtMethod = dto.OtMethod;
        policy.OtRateNormalDay = dto.OtRateNormalDay;
        policy.OtRatePublicHoliday = dto.OtRatePublicHoliday;
        policy.OtRateRestDay = dto.OtRateRestDay;
        policy.OtRatePublicHolidayInShift = dto.OtRatePublicHolidayInShift;
        policy.OtRateRestDayInShift = dto.OtRateRestDayInShift;
        policy.OtSalaryThreshold = dto.OtSalaryThreshold;
        policy.Temporary = dto.Temporary;
        return policy;
    }

    private static PolicyDto ToDto(EmployeePolicy p, IEnumerable<PolicyLeaveEntitlement> entitlements) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Description = p.Description,
        IsDefault = p.IsDefault,
        IsArchived = p.IsArchived,
        CanAccessAttendance = p.CanAccessAttendance,
        CanAccessClaims = p.CanAccessClaims,
        CanAccessLeave = p.CanAccessLeave,
        RequireGeofence = p.RequireGeofence,
        RequireSelfie = p.RequireSelfie,
        RequireClockOutSelfie = p.RequireClockOutSelfie,
        CaptureLocationOnBreakStart = p.CaptureLocationOnBreakStart,
        CaptureLocationOnBreakEnd = p.CaptureLocationOnBreakEnd,
        RequireIpWhitelist = p.RequireIpWhitelist,
        GeolocationEnabled = p.GeolocationEnabled,
        CaptureLocationOnClockIn = p.CaptureLocationOnClockIn,
        CaptureLocationOnClockOut = p.CaptureLocationOnClockOut,
        AutoClockOutEnabled = p.AutoClockOutEnabled,
        AutoClockOutAfterMinutes = p.AutoClockOutAfterMinutes,
        SalaryType = p.SalaryType,
        OtEnabled = p.OtEnabled,
        OtDailyThresholdMinutes = p.OtDailyThresholdMinutes,
        OtMethod = p.OtMethod,
        OtRateNormalDay = p.OtRateNormalDay,
        OtRatePublicHoliday = p.OtRatePublicHoliday,
        OtRateRestDay = p.OtRateRestDay,
        OtRatePublicHolidayInShift = p.OtRatePublicHolidayInShift,
        OtRateRestDayInShift = p.OtRateRestDayInShift,
        OtSalaryThreshold = p.OtSalaryThreshold,
        Temporary = p.Temporary,
        LeaveEntitlements = entitlements
            .Select(e => new PolicyLeaveEntitlementDto { LeaveTypeId = e.LeaveTypeId, DefaultDays = e.DefaultDays })
            .ToList(),
    };

    public async Task<IReadOnlyList<EmployeePolicy>> GetAllAcrossOrgsAsync() =>
        await _policies.GetAllAcrossOrgsAsync();

    public async Task<IReadOnlyList<PolicyLeaveEntitlement>> GetAllPolicyEntitlementsAsync() =>
        await _entitlements.GetAllAsync();
}
