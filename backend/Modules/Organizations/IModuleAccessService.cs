namespace AltomateHR.Api.Modules.Organizations;

public interface IModuleAccessService
{
    // Modules the current caller can use in their active org:
    // org ceiling (plan/tier/addons) ∩ their admin grant. Empty if no org context.
    Task<IReadOnlyCollection<string>> GetEnabledModulesAsync();
}
