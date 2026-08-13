using AltomateHR.Api.Modules.Organizations.Dtos;

namespace AltomateHR.Api.Modules.Organizations;

public interface IOrganizationService
{
    Task<OrganizationDto?> GetByIdAsync(string organizationId);
    Task<OrganizationDto?> UpdateAsync(string organizationId, UpdateOrganizationDto dto);
}
