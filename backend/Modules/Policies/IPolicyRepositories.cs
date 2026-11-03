using AltomateHR.Api.Modules.Policies.Entities;

namespace AltomateHR.Api.Modules.Policies;

public interface IEmployeePolicyRepository
{
    Task<List<EmployeePolicy>> GetAllAsync();

    // Every org's policies, bypassing the tenant filter — for background jobs
    // (auto clock-out sweep) that legitimately span orgs.
    Task<List<EmployeePolicy>> GetAllAcrossOrgsAsync();
    // The live (unarchived) policies of the NAMED orgs, bypassing the tenant
    // filter — the employee transfer offers the target org's policies.
    // (Default keeps hand-written test doubles compiling; the real repository overrides.)
    Task<List<EmployeePolicy>> GetActiveForOrgsAsync(IReadOnlyCollection<string> organizationIds) =>
        Task.FromResult(new List<EmployeePolicy>());
    // EVERY policy of a NAMED org, archived ones included, bypassing the
    // tenant filter. Archived matters: a policy explicitly ASSIGNED to someone
    // still governs them, so leaving them out would resolve that person onto
    // the default instead. (Default keeps hand-written test doubles compiling.)
    Task<List<EmployeePolicy>> GetForOrgAsync(string organizationId) =>
        Task.FromResult(new List<EmployeePolicy>());
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

public interface IPolicyOvertimeRateRepository
{
    Task<List<PolicyOvertimeRate>> GetByPolicyAsync(string policyId);

    // Every row in the company, for pricing a whole payroll run at once.
    Task<List<PolicyOvertimeRate>> GetAllAsync();
    Task ReplaceForPolicyAsync(string policyId, IEnumerable<PolicyOvertimeRate> rates);
}
