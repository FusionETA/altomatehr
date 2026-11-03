using AltomateHR.Api.Modules.Policies.Dtos;
using AltomateHR.Api.Modules.Policies.Entities;

namespace AltomateHR.Api.Modules.Policies;

public interface IPolicyService
{
    // --- Admin CRUD ---
    // Every policy in the org — for the services that resolve who is on what.
    Task<IEnumerable<PolicyDto>> GetAllAsync();
    // The policies the CALLER may see: all of them, or for an admin limited
    // to some policies, just those. What GET /policies returns.
    Task<IEnumerable<PolicyDto>> GetVisibleAsync() => GetAllAsync();
    // Owner-level (AdminAccess.ManagesAllPolicies): create, archive/restore,
    // set the default. Editing a policy needs it to be in the caller's scope.
    Task<PolicySaveResult> CreateAsync(SavePolicyDto dto);
    Task<PolicySaveResult> UpdateAsync(string id, SavePolicyDto dto);
    Task<PolicySaveResult> SetArchivedAsync(string id, bool archived);
    Task<PolicySaveResult> SetDefaultAsync(string id);

    // --- Resolution for other modules ---
    // The employee's assigned policy, or the org default when unassigned.
    Task<EmployeePolicy?> GetEffectivePolicyAsync(string employeeId);

    // Which modules this employee's policy lets them use.
    //
    // A policy governs the person's OWN use of a module — filing a claim,
    // applying for leave. It does not govern approving other people's, which is
    // decided by role and team position.
    //
    // No policy at all means full access: an org that has never configured one
    // should not have its staff locked out of everything.
    Task<PolicyModuleAccess> GetModuleAccessAsync(string employeeId);

    // Same, for MANY employees at once — one read of the org's policies rather
    // than two queries per head. Payroll generation needs every employee's OT
    // multipliers in one pass; resolving them one at a time made a 200-person
    // run 400 queries for a handful of distinct policies. Employees with no
    // policy and no org default are absent from the result.
    Task<IReadOnlyDictionary<string, EmployeePolicy>>
        GetEffectivePoliciesForEmployeesAsync(IEnumerable<string> employeeIds);
    // Whether attendance geofence enforcement applies to this employee.
    Task<bool> RequiresGeofenceAsync(string employeeId);
    // Per-leave-type entitlement overrides for the employee's policy.
    Task<IReadOnlyDictionary<string, double>> GetLeaveEntitlementsAsync(string employeeId);
    // Same, for MANY employees at once. Each distinct policy is loaded once, so
    // the org-wide balances grid costs a handful of queries instead of 2 per head.
    Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>>>
        GetLeaveEntitlementsForEmployeesAsync(IEnumerable<string> employeeIds);

    // Every policy in EVERY org — for the tenant-spanning attendance sweep,
    // which runs with no request context.
    Task<IReadOnlyList<EmployeePolicy>> GetAllAcrossOrgsAsync();

    // The raw policy-level leave entitlement rows, for the leave cron's bulk read.
    Task<IReadOnlyList<PolicyLeaveEntitlement>> GetAllPolicyEntitlementsAsync();
}

// Forbidden: the caller may not do this (→ 403). Ok false with no Error and not
// Forbidden is "no such policy" (→ 404).
public record PolicySaveResult(bool Ok, PolicyDto? Policy, string? Error, bool Forbidden = false);

public readonly record struct PolicyModuleAccess(bool Attendance, bool Claims, bool Leave)
{
    public static readonly PolicyModuleAccess All = new(true, true, true);

    public bool Allows(string module) => module switch
    {
        PolicyModules.Attendance => Attendance,
        PolicyModules.Claims => Claims,
        PolicyModules.Leave => Leave,
        _ => true,
    };
}

// The module names the gate understands. Strings rather than an enum because
// they are written into an attribute at each endpoint.
public static class PolicyModules
{
    public const string Attendance = "Attendance";
    public const string Claims = "Claims";
    public const string Leave = "Leave";
}
