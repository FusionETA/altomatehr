using System.ComponentModel.DataAnnotations;
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
    private readonly IOrgHolidayService _holidays;

    public OrganizationsController(
        IOrganizationService organizations,
        IOrgHolidayService holidays)
    {
        _organizations = organizations;
        _holidays = holidays;
    }

    // GET /organizations/holidays?year=YYYY — the org's public-holiday list.
    // Readable by any signed-in user: leave day-counting depends on it, so an
    // employee needs to see why a request cost what it did.
    [RequireScope("organizations:read")]
    [HttpGet("holidays")]
    public async Task<IActionResult> GetHolidays([FromQuery, Range(2000, 2100)] int? year) =>
        Ok(await _holidays.GetAsync(year));

    // PUT /organizations/holidays?year=YYYY — replace that YEAR's calendar.
    // A whole-year replace, so omitting a date removes it; other years are
    // untouched. Mirrors production, where an admin edits a year and PUTs it back.
    [Authorize(Roles = "Admin,Owner")]
    [HttpPut("holidays")]
    public async Task<IActionResult> SaveHolidays(
        [FromQuery, Range(2000, 2100)] int? year, SaveHolidaysDto dto)
    {
        var result = await _holidays.ReplaceYearAsync(year ?? DateTime.UtcNow.Year, dto);
        return result.Ok ? Ok(result.Holidays) : BadRequest(new { message = result.Error });
    }

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
        try
        {
            var org = await _organizations.UpdateAsync(GetOrgId(), dto);
            return org is null ? NotFound() : Ok(org);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
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
