using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Organizations.Dtos;
using AltomateHR.Api.Modules.Organizations.Entities;

namespace AltomateHR.Api.Modules.Organizations;

// Business logic for org settings. The org id always comes from the caller's JWT
// (passed in by the controller), so a user can only read/update their OWN org.
public class OrganizationService : IOrganizationService
{
    private readonly IOrganizationRepository _repo;
    private readonly IOrganizationMembershipRepository _memberships;

    public OrganizationService(IOrganizationRepository repo, IOrganizationMembershipRepository memberships)
    {
        _repo = repo;
        _memberships = memberships;
    }

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
        org.MileageUnit = dto.MileageUnit;
        org.GeofenceRadiusMeters = dto.GeofenceRadiusMeters;
        await _repo.UpdateAsync(org);

        return ToDto(org);
    }

    public async Task<OrganizationDto> CreateAsync(CreateOrganizationDto dto, string ownerUserId)
    {
        var org = new Organization
        {
            Name = dto.Name.Trim(),
            CreatedAt = DateTime.UtcNow,
            // DefaultCurrency (MYR), MileageUnit (KM), GeofenceRadiusMeters (200)
            // come from the entity defaults; the owner can edit them afterwards.
        };
        await _repo.AddAsync(org);

        // Make the creator the OWNER of the new org — otherwise they'd create a
        // company they can't access. OrganizationId is set EXPLICITLY (to the new
        // org, not the active one), so StampTenant won't override it.
        await _memberships.AddAsync(new OrganizationMembership
        {
            OrganizationId = org.Id,
            UserId = ownerUserId,
            Role = "Owner",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });

        return ToDto(org);
    }

    private static OrganizationDto ToDto(Organization o) => new()
    {
        Id = o.Id,
        Name = o.Name,
        DefaultCurrency = o.DefaultCurrency,
        DefaultMileageRate = o.DefaultMileageRate,
        MileageUnit = o.MileageUnit,
        GeofenceRadiusMeters = o.GeofenceRadiusMeters,
    };
}
