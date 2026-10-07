namespace AltomateHR.Api.Modules.Organizations;

public interface IModuleAccessService
{
    // Modules the current caller can use in their active org:
    // org ceiling (plan/tier/addons) ∩ their admin grant. Empty if no org context.
    Task<IReadOnlyCollection<string>> GetEnabledModulesAsync();

    // The org's ceiling alone — what its plan includes, ignoring any grant.
    // What an employee-side endpoint is gated by: an Admin without the Claims
    // grant is still an employee who files their own claims.
    Task<IReadOnlyCollection<string>> GetOrgModulesAsync();

    // The caller's admin limits — module levels, employee scope, settings.
    // Loaded once per request. (Defaults keep hand-written test doubles
    // compiling: full access.)
    Task<AdminAccess> GetAccessAsync() => Task.FromResult(AdminAccess.Full);

    // A module's level for the caller: None when the plan lacks it or their
    // grant leaves it off, else View or Manage.
    async Task<ModuleLevel> GetModuleLevelAsync(string module) =>
        (await GetEnabledModulesAsync()).Contains(module, StringComparer.OrdinalIgnoreCase)
            ? ModuleLevel.Manage
            : ModuleLevel.None;
}
