using AltomateHR.Api.Modules.ApiKeys.Dtos;
using AltomateHR.Api.Modules.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltomateHR.Api.Modules.ApiKeys;

// SUPERADMIN-only management of machine credentials for the caller's active org.
// wp_live_ tokens are a Fusioneta-support concern, NOT a customer action — the same rule
// as the monolith (the Settings → API tab renders only for superadmins). Gating to the
// Superadmin policy also means a wp_live_ key (no email claim) can never mint or revoke
// keys — no privilege escalation.
[ApiController]
[Route("api-keys")]
[Authorize(Policy = AuthPolicies.Superadmin)]
public class ApiKeysController : ControllerBase
{
    private readonly IApiKeyService _service;

    public ApiKeysController(IApiKeyService service) => _service = service;

    // POST /api-keys — create a key. The raw token is in the response body ONCE.
    [HttpPost]
    public async Task<IActionResult> Create(CreateApiKeyDto dto)
    {
        try
        {
            return Ok(await _service.CreateAsync(dto));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = new { status = 400, message = ex.Message } });
        }
    }

    // GET /api-keys — list keys for the current org (prefixes only, never the token).
    [HttpGet]
    public async Task<IActionResult> GetAll() => Ok(await _service.GetAllAsync());

    // DELETE /api-keys/{id} — revoke (soft). Idempotent.
    [HttpDelete("{id}")]
    public async Task<IActionResult> Revoke(string id) =>
        await _service.RevokeAsync(id) ? NoContent() : NotFound();
}
