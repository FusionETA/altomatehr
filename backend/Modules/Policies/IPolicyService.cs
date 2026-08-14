using AltomateHR.Api.Modules.Policies.Dtos;
using AltomateHR.Api.Modules.Policies.Entities;

namespace AltomateHR.Api.Modules.Policies;

public interface IPolicyService
{
    // --- Admin CRUD ---
    Task<IEnumerable<PolicyDto>> GetAllAsync();
    Task<PolicySaveResult> CreateAsync(SavePolicyDto dto);
    Task<PolicySaveResult> UpdateAsync(string id, SavePolicyDto dto);
    Task<PolicyDto?> SetArchivedAsync(string id, bool archived);
    Task<PolicyDto?> SetDefaultAsync(string id);

    // --- Resolution for other modules ---
    // The employee's assigned policy, or the org default when unassigned.
    Task<EmployeePolicy?> GetEffectivePolicyAsync(string employeeId);
    // Whether attendance geofence enforcement applies to this employee.
    Task<bool> RequiresGeofenceAsync(string employeeId);
    // Per-leave-type entitlement overrides for the employee's policy.
    Task<IReadOnlyDictionary<string, double>> GetLeaveEntitlementsAsync(string employeeId);
}

public record PolicySaveResult(bool Ok, PolicyDto? Policy, string? Error);
