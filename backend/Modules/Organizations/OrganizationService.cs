using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Organizations.Dtos;
using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Xero;
using AltomateHR.Api.Modules.Claims.Entities;
using AltomateHR.Api.Modules.Xero.Dtos;
using AltomateHR.Api.Modules.Organizations.Entities;

namespace AltomateHR.Api.Modules.Organizations;

// Business logic for org settings. The org id always comes from the caller's JWT
// (passed in by the controller), so a user can only read/update their OWN org.
public class OrganizationService : IOrganizationService
{
    private readonly IOrganizationRepository _repo;
    private readonly IXeroService _xero;
    private readonly IAuditService _audit;
    private readonly IOrganizationMembershipRepository _memberships;

    public OrganizationService(
        IOrganizationRepository repo,
        IOrganizationMembershipRepository memberships,
        IAuditService audit,
        IXeroService xero)
    {
        _repo = repo;
        _memberships = memberships;
        _audit = audit;
        _xero = xero;
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

        // Every claim is denominated in this, and Xero refuses a bill in a
        // currency the organisation is not subscribed to. Caught here rather
        // than at sync time: otherwise one wrong setting silently breaks EVERY
        // subsequent claim, and the error surfaces weeks later on a bill.
        //
        // Only enforced while Xero is connected — an org that has not linked it
        // has nothing to check against, and refusing every currency then would
        // make the field unusable.
        var currency = dto.DefaultCurrency.Trim().ToUpperInvariant();
        var allowed = await _xero.GetCurrenciesAsync();
        if (allowed.Count > 0 &&
            !allowed.Any(c => string.Equals(c.Code, currency, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                $"Xero is not subscribed to {currency}. "
                + $"Pick one it holds ({string.Join(", ", allowed.Select(c => c.Code))}), "
                + "or add the currency in Xero first.");
        }

        org.Name = dto.Name;
        org.DefaultCurrency = currency;
        org.DefaultMileageRate = dto.DefaultMileageRate;
        org.MileageUnit = dto.MileageUnit;
        org.GeofenceRadiusMeters = dto.GeofenceRadiusMeters;
        org.WorkingDays = string.IsNullOrWhiteSpace(dto.WorkingDays) ? null : dto.WorkingDays.Trim();
        org.WorkingHoursStart = dto.WorkingHoursStart;
        org.WorkingHoursEnd = dto.WorkingHoursEnd;
        await _repo.UpdateAsync(org);

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.SettingsOrgUpdate,
            org.Name,
            TargetType: "Organization",
            TargetId: org.Id,
            Metadata: new
            {
                org.Name,
                org.DefaultCurrency,
                org.DefaultMileageRate,
                MileageUnit = org.MileageUnit.ToString(),
                org.GeofenceRadiusMeters,
                org.WorkingHoursStart,
                org.WorkingHoursEnd,
                org.WorkingDays,
            }));

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

    public async Task<OrganizationDto?> SetClaimSettingsAsync(
        string organizationId, int cutoffDay, ClaimSettlement settlementRoute, XeroBillStatus xeroBillStage)
    {
        // Capped at 28: a cutoff of 30 would silently not exist in February, and
        // "the run closed on a day that never came" is the worst possible bug in
        // a month-end process.
        if (cutoffDay is < 1 or > 28)
            throw new ArgumentException("Cutoff day must be between 1 and 28.");

        var org = await _repo.GetByIdAsync(organizationId);
        if (org is null) return null;

        org.ClaimRunCutoffDay = cutoffDay;
        org.ClaimSettlementRoute = settlementRoute;
        org.XeroBillStage = xeroBillStage;
        await _repo.UpdateAsync(org);

        // Where approved money goes and when the run closes — the two claim
        // settings someone would most want a date and a name against.
        await _audit.WriteAsync(new AuditEvent(
            AuditActions.SettingsClaimsUpdate,
            $"{settlementRoute} · run closes day {cutoffDay}",
            TargetType: "Organization",
            TargetId: org.Id,
            Metadata: new
            {
                CutoffDay = cutoffDay,
                SettlementRoute = settlementRoute.ToString(),
                XeroBillStage = xeroBillStage.ToString(),
            }));

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
        ClaimRunCutoffDay = o.ClaimRunCutoffDay,
        ClaimSettlementRoute = o.ClaimSettlementRoute.ToString(),
        XeroBillStage = o.XeroBillStage.ToString(),
        Plan = o.Plan.ToString(),
        Tier = o.Tier?.ToString(),
        Addons = OrgModules.Split(o.Addons),
        EnabledModules = OrgModules
            .DeriveOrgEnabledModules(o.Plan, o.Tier, OrgModules.Split(o.Addons))
            .ToList(),
    };
}
