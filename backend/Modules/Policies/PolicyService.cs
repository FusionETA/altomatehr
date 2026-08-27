using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Policies.Dtos;
using AltomateHR.Api.Modules.Policies.Entities;

namespace AltomateHR.Api.Modules.Policies;

// Owns policy CRUD and resolves an employee's effective policy for other
// modules. Reads the employee's PolicyId from their active-org membership
// (a deliberate cross-module read — policy resolution is a Policy concern).
public class PolicyService : IPolicyService
{
    private readonly IEmployeePolicyRepository _policies;
    private readonly IPolicyLeaveEntitlementRepository _entitlements;
    private readonly IOrganizationMembershipRepository _memberships;

    public PolicyService(
        IEmployeePolicyRepository policies,
        IPolicyLeaveEntitlementRepository entitlements,
        IOrganizationMembershipRepository memberships)
    {
        _policies = policies;
        _entitlements = entitlements;
        _memberships = memberships;
    }

    public async Task<IEnumerable<PolicyDto>> GetAllAsync()
    {
        var policies = await _policies.GetAllAsync();
        var result = new List<PolicyDto>(policies.Count);
        foreach (var policy in policies)
            result.Add(ToDto(policy, await _entitlements.GetByPolicyAsync(policy.Id)));
        return result;
    }

    public async Task<PolicySaveResult> CreateAsync(SavePolicyDto dto)
    {
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
        if (policy is null)
            return new PolicySaveResult(false, null, null);   // → 404

        var name = dto.Name.Trim();
        var clash = await _policies.GetByNameAsync(name);
        if (clash is not null && clash.Id != id)
            return new PolicySaveResult(false, null, $"A policy named \"{name}\" already exists.");

        Apply(policy, dto, name);
        policy.UpdatedAt = DateTime.UtcNow;
        await _policies.UpdateAsync(policy);
        await SaveEntitlementsAsync(policy.Id, dto.LeaveEntitlements);

        return new PolicySaveResult(true, ToDto(policy, ToEntities(policy.Id, dto.LeaveEntitlements)), null);
    }

    public async Task<PolicyDto?> SetArchivedAsync(string id, bool archived)
    {
        var policy = await _policies.GetByIdAsync(id);
        if (policy is null) return null;

        policy.IsArchived = archived;
        if (archived) policy.IsDefault = false;   // an archived policy can't be the default
        policy.UpdatedAt = DateTime.UtcNow;
        await _policies.UpdateAsync(policy);
        return ToDto(policy, await _entitlements.GetByPolicyAsync(policy.Id));
    }

    public async Task<PolicyDto?> SetDefaultAsync(string id)
    {
        var policy = await _policies.GetByIdAsync(id);
        if (policy is null || policy.IsArchived) return null;

        policy.IsDefault = true;
        policy.UpdatedAt = DateTime.UtcNow;
        await _policies.UpdateAsync(policy);
        await _policies.ClearDefaultExceptAsync(policy.Id);   // exactly one default
        return ToDto(policy, await _entitlements.GetByPolicyAsync(policy.Id));
    }

    public async Task<EmployeePolicy?> GetEffectivePolicyAsync(string employeeId)
    {
        var membership = await _memberships.GetForUserInCurrentOrgAsync(employeeId);
        var policy = membership?.PolicyId is not null ? await _policies.GetByIdAsync(membership.PolicyId) : null;
        return policy ?? await _policies.GetDefaultAsync();
    }

    public async Task<bool> RequiresGeofenceAsync(string employeeId) =>
        (await GetEffectivePolicyAsync(employeeId))?.RequireGeofence ?? true;

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
        policy.SalaryType = dto.SalaryType;
        policy.OtEnabled = dto.OtEnabled;
        policy.OtDailyThresholdMinutes = dto.OtDailyThresholdMinutes;
        policy.OtMethod = dto.OtMethod;
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
        SalaryType = p.SalaryType,
        OtEnabled = p.OtEnabled,
        OtDailyThresholdMinutes = p.OtDailyThresholdMinutes,
        OtMethod = p.OtMethod,
        Temporary = p.Temporary,
        LeaveEntitlements = entitlements
            .Select(e => new PolicyLeaveEntitlementDto { LeaveTypeId = e.LeaveTypeId, DefaultDays = e.DefaultDays })
            .ToList(),
    };
}
