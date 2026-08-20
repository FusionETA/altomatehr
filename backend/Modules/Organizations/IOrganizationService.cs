using AltomateHR.Api.Modules.Organizations.Dtos;

namespace AltomateHR.Api.Modules.Organizations;

public interface IOrganizationService
{
    Task<OrganizationDto?> GetByIdAsync(string organizationId);

    // Create a new company and make `ownerUserId` its Owner (so they can access it).
    Task<OrganizationDto> CreateAsync(CreateOrganizationDto dto, string ownerUserId);

    Task<OrganizationDto?> UpdateAsync(string organizationId, UpdateOrganizationDto dto);

    // Provision/change the org's package (plan + tier + addons). Returns null if the org
    // is missing; throws ArgumentException on an invalid plan/tier/addon value.
    Task<OrganizationDto?> UpdatePlanAsync(string organizationId, UpdateOrgPlanDto dto);
}
