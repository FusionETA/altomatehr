using AltomateHR.Api.Modules.ApiKeys.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltomateHR.Api.Modules.ApiKeys;

// Owner-only management of machine credentials for the CURRENT org.
// Gated to "Owner": a wp_live_ key authenticates as "Admin", so a key can never create
// or revoke keys — no privilege escalation.
[ApiController]
[Route("api-keys")]
[Authorize(Roles = "Owner")]
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
