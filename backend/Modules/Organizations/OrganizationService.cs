using AltomateHR.Api.Modules.Organizations.Dtos;
using AltomateHR.Api.Modules.Organizations.Entities;

namespace AltomateHR.Api.Modules.Organizations;

// Business logic for org settings. The org id always comes from the caller's JWT
// (passed in by the controller), so a user can only read/update their OWN org.
public class OrganizationService : IOrganizationService
{
    private readonly IOrganizationRepository _repo;

    public OrganizationService(IOrganizationRepository repo) => _repo = repo;

    public async Task<OrganizationDto?> GetByIdAsync(string organizationId)
    {
        var org = await _repo.GetByIdAsync(organizationId);
        return org is null ? null : ToDto(org);
    }

    public async Task<OrganizationDto?> UpdateAsync(string organizationId, UpdateOrganizationDto dto)
    {
        var org = await _repo.GetByIdAsync(organizationId);
        if (org is null) return null;

        org.Name = dto.Name;
        org.DefaultCurrency = dto.DefaultCurrency;
        org.DefaultMileageRate = dto.DefaultMileageRate;
        org.GeofenceRadiusMeters = dto.GeofenceRadiusMeters;
        await _repo.UpdateAsync(org);

        return ToDto(org);
    }

    private static OrganizationDto ToDto(Organization o) => new()
    {
        Id = o.Id,
        Name = o.Name,
        DefaultCurrency = o.DefaultCurrency,
        DefaultMileageRate = o.DefaultMileageRate,
        GeofenceRadiusMeters = o.GeofenceRadiusMeters,
    };
}
