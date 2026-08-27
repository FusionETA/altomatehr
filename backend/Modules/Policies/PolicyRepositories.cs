using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Policies.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Policies;

public class EmployeePolicyRepository : IEmployeePolicyRepository
{
    private readonly AppDbContext _db;

    public EmployeePolicyRepository(AppDbContext db) => _db = db;

    public Task<List<EmployeePolicy>> GetAllAcrossOrgsAsync() =>
        _db.EmployeePolicies.IgnoreQueryFilters().ToListAsync();

    public Task<List<EmployeePolicy>> GetAllAsync() =>
        _db.EmployeePolicies.OrderBy(p => p.Name).ToListAsync();

    public Task<EmployeePolicy?> GetByIdAsync(string id) =>
        _db.EmployeePolicies.FirstOrDefaultAsync(p => p.Id == id);

    public Task<EmployeePolicy?> GetByNameAsync(string name) =>
        _db.EmployeePolicies.FirstOrDefaultAsync(p => p.Name == name);

    public Task<EmployeePolicy?> GetDefaultAsync() =>
        _db.EmployeePolicies.FirstOrDefaultAsync(p => p.IsDefault && !p.IsArchived);

    public async Task<EmployeePolicy> AddAsync(EmployeePolicy policy)
    {
        _db.EmployeePolicies.Add(policy);
        await _db.SaveChangesAsync();   // OrganizationId auto-stamped here
        return policy;
    }

    public async Task UpdateAsync(EmployeePolicy policy)
    {
        _db.EmployeePolicies.Update(policy);
        await _db.SaveChangesAsync();
    }

    // Ensure a single default: clear IsDefault on every other policy.
    public async Task ClearDefaultExceptAsync(string keepId)
    {
        var others = await _db.EmployeePolicies
            .Where(p => p.IsDefault && p.Id != keepId)
            .ToListAsync();
        foreach (var p in others) p.IsDefault = false;
        if (others.Count > 0) await _db.SaveChangesAsync();
    }
}

public class PolicyLeaveEntitlementRepository : IPolicyLeaveEntitlementRepository
{
    private readonly AppDbContext _db;

    public PolicyLeaveEntitlementRepository(AppDbContext db) => _db = db;

    public Task<List<PolicyLeaveEntitlement>> GetByPolicyAsync(string policyId) =>
        _db.PolicyLeaveEntitlements.Where(e => e.PolicyId == policyId).ToListAsync();

    public async Task ReplaceForPolicyAsync(string policyId, IEnumerable<PolicyLeaveEntitlement> entitlements)
    {
        var existing = await _db.PolicyLeaveEntitlements.Where(e => e.PolicyId == policyId).ToListAsync();
        _db.PolicyLeaveEntitlements.RemoveRange(existing);
        _db.PolicyLeaveEntitlements.AddRange(entitlements);
        await _db.SaveChangesAsync();
    }
}
