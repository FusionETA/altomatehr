using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using AltomateHR.Api.Modules.Claims.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltomateHR.Api.Modules.Claims;

[ApiController]
[Route("[controller]")]        // → /claims
[Authorize]                    // every endpoint here now requires a valid JWT
public class ClaimsController : ControllerBase
{
    private readonly IClaimsService _claims;

    public ClaimsController(IClaimsService claims) => _claims = claims;

    // GET /claims — the caller's own claims.
    [HttpGet]
    public async Task<IActionResult> GetMine() =>
        Ok(await _claims.GetMineAsync(GetUserId()));

    // GET /claims/team — claims the caller can approve (their direct reports;
    // the whole org for admins/owners).
    [Authorize(Roles = "Admin,Owner,Supervisor")]
    [HttpGet("team")]
    public async Task<IActionResult> GetTeam() =>
        Ok(await _claims.GetTeamAsync(GetUserId(), GetRole()));

    // GET /claims/{id}
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var claim = await _claims.GetVisibleByIdAsync(id, GetUserId(), User.IsInRole("Admin"));
        return claim is null ? NotFound() : Ok(claim);
    }

    // POST /claims
    [HttpPost]
    public async Task<IActionResult> Create(CreateClaimDto dto)
    {
        var claim = await _claims.CreateAsync(dto, GetUserId());
        return CreatedAtAction(nameof(GetById), new { id = claim.Id }, claim);
    }

    // POST /claims/receipts
    [HttpPost("receipts")]
    [RequestSizeLimit(8 * 1024 * 1024)]
    public async Task<ActionResult<UploadReceiptResponseDto>> UploadReceipt(IFormFile? receiptFile)
    {
        if (receiptFile is null || receiptFile.Length == 0)
            return BadRequest(new { message = "Pick a receipt file to upload." });

        try
        {
            await using var stream = receiptFile.OpenReadStream();
            var result = await _claims.StoreReceiptAsync(new ClaimReceiptUpload(
                receiptFile.FileName,
                receiptFile.ContentType,
                receiptFile.Length,
                stream));

            return Ok(new UploadReceiptResponseDto { ReceiptUrl = result.ReceiptUrl });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // GET /claims/receipts/{fileName}
    [HttpGet("receipts/{fileName}")]
    public async Task<IActionResult> GetReceipt(string fileName)
    {
        var receipt = await _claims.GetReceiptForUserAsync(
            fileName,
            GetUserId(),
            User.IsInRole("Admin"));

        if (receipt is null)
            return NotFound();

        Response.Headers.CacheControl = "no-store";
        return PhysicalFile(receipt.Path, receipt.ContentType, receipt.DownloadName);
    }

    // PUT /claims/{id}
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, CreateClaimDto dto)
    {
        var ok = await _claims.UpdateAsync(id, dto, GetUserId(), User.IsInRole("Admin"));
        return ok ? NoContent() : NotFound();
    }

    // DELETE /claims/{id}  — Admins only (RBAC)
    [Authorize(Roles = "Admin")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var ok = await _claims.DeleteAsync(id);
        return ok ? NoContent() : NotFound();
    }

    // POST /claims/{id}/approve — supervisor of the claimant (or admin/owner).
    [Authorize(Roles = "Admin,Owner,Supervisor")]
    [HttpPost("{id}/approve")]
    public async Task<IActionResult> Approve(string id)
    {
        var result = await _claims.ApproveAsync(id, GetUserId(), GetRole());
        return ToStatusTransitionResponse(result);
    }

    // POST /claims/{id}/reject — supervisor of the claimant (or admin/owner).
    [Authorize(Roles = "Admin,Owner,Supervisor")]
    [HttpPost("{id}/reject")]
    public async Task<IActionResult> Reject(string id, RejectClaimDto dto)
    {
        var result = await _claims.RejectAsync(id, GetUserId(), GetRole(), dto.ReviewNotes);
        return ToStatusTransitionResponse(result);
    }

    private string GetUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub)
        ?? throw new InvalidOperationException("Authenticated user id is missing.");

    private string? GetRole() => User.FindFirstValue(ClaimTypes.Role);

    private IActionResult ToStatusTransitionResponse(ClaimStatusTransitionResult result)
    {
        if (!result.Found)
            return NotFound();

        if (!result.Transitioned)
            return BadRequest(new { message = result.ErrorMessage });

        return Ok(result.Claim);
    }
}
