using AltomateHR.Api.Modules.Claims.Entities;
using AltomateHR.Api.Modules.Xero.Dtos;
using AltomateHR.Api.Modules.Organizations.Dtos;

namespace AltomateHR.Api.Modules.Organizations;

public interface IOrganizationService
{
    Task<OrganizationDto?> GetByIdAsync(string organizationId);

    // Create a new company and make `ownerUserId` its Owner (so they can access it).
    Task<OrganizationDto> CreateAsync(CreateOrganizationDto dto, string ownerUserId);

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
