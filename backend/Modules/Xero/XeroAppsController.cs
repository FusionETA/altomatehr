using AltomateHR.Api.Modules.ApiKeys;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Xero.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltomateHR.Api.Modules.Xero;

// The superadmin's Xero apps: which Xero app each owner's companies connect
// through. Superadmins only, never an API key; a secret goes in and never
// comes back out.
[ApiController]
[Route("support/xero")]
[Authorize(Policy = AuthPolicies.Superadmin)]
[HumanOnly]
public class XeroAppsController : ControllerBase
{
    private readonly IXeroAppsService _apps;

    public XeroAppsController(IXeroAppsService apps) => _apps = apps;

    // GET /support/xero — the apps, and every owner with their companies.
    [HttpGet]
    public async Task<ActionResult<XeroAppsOverviewDto>> Overview() => Ok(await _apps.GetOverviewAsync());

    // POST /support/xero/apps
    [HttpPost("apps")]
    public async Task<IActionResult> AddApp(SaveXeroAppDto dto)
    {
        try
        {
            return Ok(await _apps.AddAppAsync(dto));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // PUT /support/xero/apps/{id} — a blank secret keeps the saved one.
    [HttpPut("apps/{id}")]
    public async Task<IActionResult> UpdateApp(string id, SaveXeroAppDto dto)
    {
        try
        {
            var app = await _apps.UpdateAppAsync(id, dto);
            return app is null ? NotFound() : Ok(app);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // DELETE /support/xero/apps/{id}
    [HttpDelete("apps/{id}")]
    public async Task<IActionResult> DeleteApp(string id)
    {
        var (notFound, error) = await _apps.DeleteAppAsync(id);
        if (notFound) return NotFound();
        return error is null ? NoContent() : Conflict(new { message = error });
    }

    // PUT /support/xero/owners/{userId} — Shared, Original, or Standalone (+ app).
    [HttpPut("owners/{userId}")]
    public async Task<IActionResult> SetOwnerChoice(string userId, SetXeroOwnerChoiceDto dto)
    {
        try
        {
            await _apps.SetOwnerChoiceAsync(userId, dto);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // PUT /support/xero/companies/{id}/owner — which owner a co-owned company follows.
    [HttpPut("companies/{id}/owner")]
    public async Task<IActionResult> SetCompanyOwner(string id, SetXeroCompanyOwnerDto dto)
    {
        var (notFound, error) = await _apps.SetCompanyOwnerAsync(id, dto.OwnerUserId);
        if (notFound) return NotFound();
        return error is null ? NoContent() : BadRequest(new { message = error });
    }
}
