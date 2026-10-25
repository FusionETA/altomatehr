using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using AltomateHR.Api.Modules.Organizations.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AltomateHR.Api.Modules.ApiKeys;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Organizations;

[ApiController]
[Route("[controller]")]        // → /organizations
[Authorize]
public class OrganizationsController : ControllerBase
{
    private readonly IOrganizationService _organizations;
    private readonly ICurrentUser _currentUser;

    public OrganizationsController(IOrganizationService organizations, ICurrentUser currentUser)
    {
        _organizations = organizations;
        _currentUser = currentUser;
    }


    // GET /organizations/current — the caller's own org (any authenticated user can read it).
    [RequireScope("organizations:read")]
    [HttpGet("current")]
    public async Task<IActionResult> GetCurrent()
    {
        var org = await _organizations.GetByIdAsync(GetOrgId());
        return org is null ? NotFound() : Ok(org);
    }

    // GET /organizations/modules — the caller's effective enabled modules (the
    // org plan ceiling ∩ their per-admin grant) plus every grantable key. Drives
    // nav visibility and the Owner's access picker. Any authenticated user reads
    // their own access.
    [RequireScope("organizations:read")]
    [HttpGet("modules")]
    public async Task<IActionResult> GetModules([FromServices] IModuleAccessService access)
    {
        var enabled = await access.GetEnabledModulesAsync();
        var mine = await access.GetAccessAsync();
        return Ok(new ModuleAccessDto
        {
            All = OrgModules.GrantableModules,
            Enabled = enabled,
            Levels = enabled.ToDictionary(m => m, mine.LevelFor, StringComparer.OrdinalIgnoreCase),
            CanChangeSettings = mine.CanChangeSettings,
            AllEmployees = mine.HasFullEmployeeScope,
            ManagesAllPolicies = mine.ManagesAllPolicies,
        });
    }

    // GET /organizations/admins — the org's admins with their module grant.
    // Owners only: controlling who sees what is the Owner's call, not an admin's.
    [Authorize(Roles = "Owner")]
    [HumanOnly]
    [HttpGet("admins")]
    public async Task<IActionResult> ListAdmins() => Ok(await _organizations.ListAdminsAsync());

    // PUT /organizations/admins/{userId}/access — set one admin's module grant
    // (null = full access). Owners only.
    [Authorize(Roles = "Owner")]
    [HumanOnly]
    [HttpPut("admins/{userId}/access")]
    public async Task<IActionResult> SetAdminAccess(string userId, SetAdminAccessDto dto)
    {
        try
        {
            var result = await _organizations.SetAdminAccessAsync(userId, dto);
            return result is null ? NotFound() : Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // PUT /organizations/admins/{userId} — change an Admin's name and login
    // email. Owners only. Both are on the person's account, so they change in
    // every company that person belongs to.
    [Authorize(Roles = "Owner")]
    [HumanOnly]
    [HttpPut("admins/{userId}")]
    public async Task<IActionResult> UpdateAdminDetails(string userId, UpdateAdminDetailsDto dto)
    {
        var (notFound, error, admin) = await _organizations.UpdateAdminDetailsAsync(userId, dto);
        if (notFound) return NotFound();
        return error is null ? Ok(admin) : BadRequest(new { message = error });
    }

    // DELETE /organizations/admins/{userId} — remove an Admin from this company.
    // Owners only. Their login stays; their access here ends at once.
    [Authorize(Roles = "Owner")]
    [HumanOnly]
    [HttpDelete("admins/{userId}")]
    public async Task<IActionResult> RemoveAdmin(string userId)
    {
        var (notFound, error) = await _organizations.RemoveAdminAsync(userId);
        if (notFound) return NotFound();
        return error is null ? NoContent() : BadRequest(new { message = error });
    }

    // PUT /organizations/current — update org settings (Admins only).
    [Authorize(Roles = "Admin,Owner")]
    [RequireScope("organizations:write")]
    [RequireSettings]
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

    // ─── Company logo (printed on Documents letters) ─────────────────────

    // GET /organizations/current/logo — the image bytes; 404 when there is
    // none. Never a public URL: served only here, never cached.
    [Authorize(Roles = "Admin,Owner")]
    [RequireScope("organizations:read")]
    [HttpGet("current/logo")]
    public async Task<IActionResult> GetLogo([FromServices] IOrganizationLogoService logos)
    {
        var logo = await logos.GetAsync(GetOrgId());
        if (logo is null) return NotFound();

        Response.Headers.CacheControl = "no-store";
        return File(logo.Content, logo.ContentType);
    }

    // PUT /organizations/current/logo — multipart, field "file": PNG, JPEG or
    // WebP (checked by its bytes), ≤ 2 MB, each side ≤ 4000 px. Replaces any
    // previous logo. Gated like PUT /organizations/current.
    [Authorize(Roles = "Admin,Owner")]
    [RequireScope("organizations:write")]
    [RequireSettings]
    [HttpPut("current/logo")]
    [RequestSizeLimit(3 * 1024 * 1024)]   // the 2 MB rule itself is the service's, with a clear 400
    public async Task<IActionResult> SetLogo(IFormFile? file, [FromServices] IOrganizationLogoService logos)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "Choose an image file (PNG, JPG or WebP) to upload." });

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer);
        var result = await logos.SetAsync(GetOrgId(), buffer.ToArray());
        if (result.Ok) return Ok(result.Organization);
        return result.Error is null ? NotFound() : BadRequest(new { error = result.Error });
    }

    // DELETE /organizations/current/logo — 204, also when there is none.
    [Authorize(Roles = "Admin,Owner")]
    [RequireScope("organizations:write")]
    [RequireSettings]
    [HttpDelete("current/logo")]
    public async Task<IActionResult> RemoveLogo([FromServices] IOrganizationLogoService logos)
    {
        await logos.RemoveAsync(GetOrgId());
        return NoContent();
    }

    // PUT /organizations/{organizationId}/plan — provision a tenant's package (plan / tier /
    // addons), which drives module access. SUPERADMIN-ONLY: this is a Fusioneta-internal
    // billing action, never something a customer Owner can do to their own org. The target
    // org is explicit, so a superadmin can provision ANY org (not just their active one).
    [Authorize(Policy = AuthPolicies.Superadmin)]
    [HumanOnly]
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

    // POST /organizations — create a new company. Admins and Owners can; the
    // creator becomes the Owner of the new org, so it appears in their org
    // switcher (GET /auth/orgs).
    [Authorize(Roles = "Admin,Owner")]
    [HumanOnly]
    [HttpPost]
    public async Task<IActionResult> Create(CreateOrganizationDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (userId is null) return Unauthorized();
        try
        {
            return Ok(await _organizations.CreateAsync(
                dto, userId, viaSso: _currentUser.IsSso, viaSupport: _currentUser.IsSupport));
        }
        catch (Exception ex) when (ex is SsoManagedActionException or SupportModeActionException)
        {
            // { message }, as ChangePassword's SSO refusal — what the frontend's
            // api-client reads, so the reason reaches the screen.
            return StatusCode(403, new { message = ex.Message });
        }
    }

    // The org id comes from the JWT 'org' claim — never from the client.
    private string GetOrgId() =>
        User.FindFirstValue("org")
        ?? throw new InvalidOperationException("Organization claim is missing.");
}
