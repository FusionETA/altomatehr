using AltomateHR.Api.Modules.Policies.Entities;

namespace AltomateHR.Api.Modules.Policies;

public interface IEmployeePolicyRepository
{
    Task<List<EmployeePolicy>> GetAllAsync();

    // Every org's policies, bypassing the tenant filter — for background jobs
    // (auto clock-out sweep) that legitimately span orgs.
    Task<List<EmployeePolicy>> GetAllAcrossOrgsAsync();
    Task<EmployeePolicy?> GetByIdAsync(string id);
    Task<EmployeePolicy?> GetByNameAsync(string name);
    Task<EmployeePolicy?> GetDefaultAsync();
    Task<EmployeePolicy> AddAsync(EmployeePolicy policy);
    Task UpdateAsync(EmployeePolicy policy);
    Task ClearDefaultExceptAsync(string keepId);
}

public interface IPolicyLeaveEntitlementRepository
{
    Task<List<PolicyLeaveEntitlement>> GetByPolicyAsync(string policyId);

    // EVERY per-policy entitlement row, for building in-memory indexes.
    // Used by the crons so per-row resolution costs no queries.
    Task<List<PolicyLeaveEntitlement>> GetAllAsync();
    Task ReplaceForPolicyAsync(string policyId, IEnumerable<PolicyLeaveEntitlement> entitlements);
}
