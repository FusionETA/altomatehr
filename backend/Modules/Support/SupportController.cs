using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Support.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AltomateHR.Api.Modules.ApiKeys;

namespace AltomateHR.Api.Modules.Support;

// Fusioneta support page — superadmins only (SUPERADMIN_EMAILS). Entering a
// company is POST /auth/support/enter/{id} (it sets the session cookie, so it
// lives with auth); a company's plan is PUT /organizations/{id}/plan.
[ApiController]
[Route("support")]
[Authorize(Policy = AuthPolicies.Superadmin)]
public class SupportController : ControllerBase
{
    private readonly ISupportService _support;
    private readonly ISupportLoginBlockService _loginBlocks;

    public SupportController(ISupportService support, ISupportLoginBlockService loginBlocks)
    {
        _support = support;
        _loginBlocks = loginBlocks;
    }

    // GET /support/organizations?search= — every company, by name or owner email.
    [HumanOnly]
    [HttpGet("organizations")]
    public async Task<IActionResult> Organizations([FromQuery] string? search) =>
        Ok(await _support.ListOrganizationsAsync(search));

    // POST /support/organizations — provision a company and its Owner.
    [HumanOnly]
    [HttpPost("organizations")]
    public async Task<IActionResult> CreateCompany(CreateSupportCompanyDto dto)
    {
        try
        {
            return Ok(await _support.CreateCompanyAsync(dto));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // POST /support/organizations/{id}/deactivate — close a company to
    // everyone in it (data kept). A reason is required.
    [HumanOnly]
    [HttpPost("organizations/{organizationId}/deactivate")]
    public async Task<IActionResult> Deactivate(string organizationId, DeactivateCompanyDto dto)
    {
        try
        {
            var row = await _support.DeactivateAsync(organizationId, dto.Reason);
            return row is null ? NotFound() : Ok(row);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // GET /support/login-blocks — accounts locked and IPs blocked from
    // signing in right now, after too many wrong passwords.
    [HumanOnly]
    [HttpGet("login-blocks")]
    public async Task<IActionResult> LoginBlocks() => Ok(await _loginBlocks.ListAsync());

    // POST /support/login-blocks/unblock — lift one early. 404 when it had
    // already run out (or been lifted).
    [HumanOnly]
    [HttpPost("login-blocks/unblock")]
    public async Task<IActionResult> Unblock(UnblockLoginDto dto) =>
        await _loginBlocks.UnblockAsync(dto.Kind!.Value, dto.Key) ? NoContent() : NotFound();

    // POST /support/organizations/{id}/reactivate — open it again.
    [HumanOnly]
    [HttpPost("organizations/{organizationId}/reactivate")]
    public async Task<IActionResult> Reactivate(string organizationId)
    {
        var row = await _support.ReactivateAsync(organizationId);
        return row is null ? NotFound() : Ok(row);
    }
}
