using System.Security.Claims;
using AltomateHR.Api.Modules.Organizations.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AltomateHR.Api.Modules.ApiKeys;
using AltomateHR.Api.Modules.Auth;

namespace AltomateHR.Api.Modules.Organizations;

[ApiController]
[Route("[controller]")]        // → /organizations
[Authorize]
public class OrganizationsController : ControllerBase
{
    private readonly IOrganizationService _organizations;

    public OrganizationsController(IOrganizationService organizations) => _organizations = organizations;

    // GET /organizations/current — the caller's own org (any authenticated user can read it).
    [RequireScope("organizations:read")]
    [HttpGet("current")]
    public async Task<IActionResult> GetCurrent()
    {
        var org = await _organizations.GetByIdAsync(GetOrgId());
        return org is null ? NotFound() : Ok(org);
    }

    // PUT /organizations/current — update org settings (Admins only).
    [Authorize(Roles = "Admin,Owner")]
    [HttpPut("current")]
    public async Task<IActionResult> UpdateCurrent(UpdateOrganizationDto dto)
    {
        var org = await _organizations.UpdateAsync(GetOrgId(), dto);
        return org is null ? NotFound() : Ok(org);
    }

    // PUT /organizations/{organizationId}/plan — provision a tenant's package (plan / tier /
    // addons), which drives module access. SUPERADMIN-ONLY: this is a Fusioneta-internal
    // billing action, never something a customer Owner can do to their own org. The target
    // org is explicit, so a superadmin can provision ANY org (not just their active one).
    [Authorize(Policy = AuthPolicies.Superadmin)]
    [HttpPut("{organizationId}/plan")]
    public async Task<IActionResult> UpdatePlan(string organizationId, UpdateOrgPlanDto dto)
    {
        try
        {
            var org = await _organizations.UpdatePlanAsync(organizationId, dto);
            return org is null ? NotFound() : Ok(org);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = new { status = 400, message = ex.Message } });
        }
    }

    // POST /organizations — create a new company. Owners only. The creator becomes
    // the Owner of the new org, so it appears in their org switcher (GET /auth/orgs).
    [Authorize(Roles = "Owner")]
    [HttpPost]
    public async Task<IActionResult> Create(CreateOrganizationDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (userId is null) return Unauthorized();
        return Ok(await _organizations.CreateAsync(dto, userId));
    }

    // The org id comes from the JWT 'org' claim — never from the client.
    private string GetOrgId() =>
        User.FindFirstValue("org")
        ?? throw new InvalidOperationException("Organization claim is missing.");
}
