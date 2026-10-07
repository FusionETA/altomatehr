using AltomateHR.Api.Modules.Claims.Entities;
using AltomateHR.Api.Modules.Xero.Dtos;
using AltomateHR.Api.Modules.Organizations.Dtos;

namespace AltomateHR.Api.Modules.Organizations;

public interface IOrganizationService
{
    Task<OrganizationDto?> GetByIdAsync(string organizationId);

    // Create a new company and make `ownerUserId` its Owner (so they can access it).
    // viaSso: the caller arrived through the SSO hand-off. Their account is
    // managed by the external platform, so creating a company here is refused
    // (SsoManagedActionException) — the UI hides "New company" for the same reason.
    Task<OrganizationDto> CreateAsync(CreateOrganizationDto dto, string ownerUserId, bool viaSso = false);

    // --- Admin access control (Owner only) ---
    // The org's admins with their current module grant, for the Owner's
    // "Manage access" surface.
    Task<IReadOnlyList<AdminAccessDto>> ListAdminsAsync();

    // Set an admin's module grant (null = full access). Returns null if the user
    // is not an Admin in this org; throws ArgumentException on an unknown module.
    Task<AdminAccessDto?> SetAdminModulesAsync(string userId, List<string>? modules);

    // Owner-only "Remove admin": take an Admin out of this company. Their login
    // stays (they may run other companies); their access here ends at once.
    // Refuses the caller themselves and any Owner. Null error = removed;
    // NotFound = no such Admin here.
    Task<(bool NotFound, string? Error)> RemoveAdminAsync(string userId) =>
        Task.FromResult<(bool, string?)>((true, null));

    // Set an admin's full access: module levels, employee scope (policies) and
    // the settings switch. Null if the user is not an Admin here; throws
    // ArgumentException on an unknown module or a policy not in this org.
    // (Default keeps hand-written test doubles compiling.)
    Task<AdminAccessDto?> SetAdminAccessAsync(string userId, SetAdminAccessDto dto) =>
        Task.FromResult<AdminAccessDto?>(null);

    Task<OrganizationDto?> UpdateAsync(string organizationId, UpdateOrganizationDto dto);

    // Sets the org's claim settings: the day of month that closes the claims run
    // (1-28) and how approved claims are paid out. The Claims module owns the
    // settings surface but the values live on the org, so it goes through here
    // rather than the Claims module touching this repository.
    // Throws ArgumentException when the cutoff day is out of range.
    Task<OrganizationDto?> SetClaimSettingsAsync(
        string organizationId, int cutoffDay, ClaimSettlement settlementRoute, XeroBillStatus xeroBillStage);

    // Provision/change the org's package (plan + tier + addons). Returns null if the org
    // is missing; throws ArgumentException on an invalid plan/tier/addon value.
    Task<OrganizationDto?> UpdatePlanAsync(string organizationId, UpdateOrgPlanDto dto);
}
