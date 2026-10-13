using AltomateHR.Api.Modules.ApiKeys;
using AltomateHR.Api.Modules.Organizations.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltomateHR.Api.Modules.Organizations;

// The company's saved signatures for Documents letters (Settings →
// Organization). Always the caller's own company: the rows are tenant-filtered,
// so another company's id is a 404 here.
//
// Reads are open to every Admin/Owner (the Documents generate dialog lists
// them); writes are gated like PUT /organizations/current and the logo.
[ApiController]
[Route("organizations/current/signatures")]
[Authorize(Roles = "Admin,Owner")]
public class OrganizationSignaturesController : ControllerBase
{
    private readonly IOrganizationSignatureService _signatures;

    public OrganizationSignaturesController(IOrganizationSignatureService signatures) => _signatures = signatures;

    // GET /organizations/current/signatures — active only, default first.
    [RequireScope("organizations:read")]
    [HttpGet]
    public async Task<IActionResult> List() => Ok(await _signatures.ListAsync());

    // GET /organizations/current/signatures/{id}/image — the image bytes.
    // Never a public URL: served only here, never cached.
    [RequireScope("organizations:read")]
    [HttpGet("{id}/image")]
    public async Task<IActionResult> GetImage(string id)
    {
        var image = await _signatures.GetImageAsync(id);
        if (image is null) return NotFound();

        Response.Headers.CacheControl = "no-store";
        return File(image.Content, image.ContentType);
    }

    // POST /organizations/current/signatures — multipart: file (PNG, JPEG or
    // WebP by its bytes, ≤ 1 MB, each side ≤ 4000 px), name, position,
    // optional isDefault ("true"/"false"). Up to 10 per company.
    [RequireScope("organizations:write")]
    [RequireSettings]
    [HttpPost]
    [RequestSizeLimit(2 * 1024 * 1024)]   // the 1 MB rule itself is the service's, with a clear 400
    public async Task<IActionResult> Add(
        IFormFile? file,
        [FromForm] string? name,
        [FromForm] string? position,
        [FromForm] string? isDefault)
    {
        var makeDefault = false;
        if (!string.IsNullOrWhiteSpace(isDefault) && !bool.TryParse(isDefault.Trim(), out makeDefault))
            return BadRequest(new { error = "isDefault must be \"true\" or \"false\"." });

        byte[]? content = null;
        if (file is { Length: > 0 })
        {
            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer);
            content = buffer.ToArray();
        }

        var result = await _signatures.AddAsync(content, name, position, makeDefault);
        if (!result.Ok) return BadRequest(new { error = result.Error });
        return StatusCode(StatusCodes.Status201Created, result.Signature);
    }

    // PUT /organizations/current/signatures/{id} — { name, position, isDefault }.
    [RequireScope("organizations:write")]
    [RequireSettings]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, UpdateSignatureDto dto)
    {
        var result = await _signatures.UpdateAsync(id, dto);
        if (result.Ok) return Ok(result.Signature);
        return result.Error is null ? NotFound() : BadRequest(new { error = result.Error });
    }

    // DELETE /organizations/current/signatures/{id} — soft remove; 204.
    [RequireScope("organizations:write")]
    [RequireSettings]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Remove(string id) =>
        await _signatures.RemoveAsync(id) ? NoContent() : NotFound();
}
