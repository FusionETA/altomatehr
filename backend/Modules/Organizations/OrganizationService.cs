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

        if (string.Compare(dto.WorkingHoursStart, dto.WorkingHoursEnd, StringComparison.Ordinal) >= 0)
            throw new ArgumentException("Working hours start must be before end.");

        org.Name = dto.Name;
        org.DefaultCurrency = dto.DefaultCurrency;
        org.DefaultMileageRate = dto.DefaultMileageRate;
        org.MileageUnit = dto.MileageUnit;
        org.GeofenceRadiusMeters = dto.GeofenceRadiusMeters;
        org.WorkingDays = string.IsNullOrWhiteSpace(dto.WorkingDays) ? null : dto.WorkingDays.Trim();
        org.WorkingHoursStart = dto.WorkingHoursStart;
        org.WorkingHoursEnd = dto.WorkingHoursEnd;
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
            // New companies start on the full package so the owner isn't locked out;
            // downgrade to FREE happens via UpdatePlanAsync (a billing action).
            Plan = OrgPlan.DIY,
            Tier = OrgPlanTier.PAID,
            Addons = "expense_claim,clock",
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

    public async Task<OrganizationDto?> UpdatePlanAsync(string organizationId, UpdateOrgPlanDto dto)
    {
        var org = await _repo.GetByIdAsync(organizationId);
        if (org is null) return null;

        if (!Enum.TryParse<OrgPlan>(dto.Plan, ignoreCase: true, out var plan))
            throw new ArgumentException($"Plan must be one of: {string.Join(", ", Enum.GetNames<OrgPlan>())}.");

        OrgPlanTier? tier = null;
        if (!string.IsNullOrWhiteSpace(dto.Tier))
        {
            if (!Enum.TryParse<OrgPlanTier>(dto.Tier, ignoreCase: true, out var parsedTier))
                throw new ArgumentException($"Tier must be one of: {string.Join(", ", Enum.GetNames<OrgPlanTier>())}.");
            tier = parsedTier;
        }

        var addons = dto.Addons.Select(a => a.Trim()).Where(a => a.Length > 0).Distinct().ToList();
        var unknown = addons.Where(a => !OrgModules.IsKnownAddon(a)).ToList();
        if (unknown.Count > 0)
            throw new ArgumentException($"Unknown addon(s): {string.Join(", ", unknown)}.");

        org.Plan = plan;
        org.Tier = tier;
        org.Addons = OrgModules.Join(addons);
        await _repo.UpdateAsync(org);

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
        WorkingDays = o.WorkingDays,
        WorkingHoursStart = o.WorkingHoursStart,
        WorkingHoursEnd = o.WorkingHoursEnd,
        Plan = o.Plan.ToString(),
        Tier = o.Tier?.ToString(),
        Addons = OrgModules.Split(o.Addons),
        EnabledModules = OrgModules
            .DeriveOrgEnabledModules(o.Plan, o.Tier, OrgModules.Split(o.Addons))
            .ToList(),
    };
}
