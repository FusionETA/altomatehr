using AltomateHR.Api.Modules.Policies.Entities;

namespace AltomateHR.Api.Modules.Policies;

public interface IEmployeePolicyRepository
{
    Task<List<EmployeePolicy>> GetAllAsync();
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
    Task ReplaceForPolicyAsync(string policyId, IEnumerable<PolicyLeaveEntitlement> entitlements);
}
