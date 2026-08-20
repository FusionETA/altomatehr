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

    // GET /claims/team — claims awaiting the caller as the current-step approver.
    [HttpGet("team")]
    [Authorize(Roles = "Supervisor,Admin,Owner")]
    public async Task<IActionResult> GetTeam() =>
        Ok(await _claims.GetTeamAsync(GetUserId()));

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
        try
        {
            var claim = await _claims.CreateAsync(dto, GetUserId());
            return CreatedAtAction(nameof(GetById), new { id = claim.Id }, claim);
        }
        catch (ClaimValidationException ex)
        {
            return BadRequest(new { message = ex.Message, field = ex.Field });
        }
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
        try
        {
            var claim = await _claims.UpdateAsync(id, dto, GetUserId(), User.IsInRole("Admin"));
            return claim is null ? NotFound() : Ok(claim);
        }
        catch (ClaimValidationException ex)
        {
            return BadRequest(new { message = ex.Message, field = ex.Field });
        }
    }

    // DELETE /claims/{id}  — Admins only (RBAC)
    [Authorize(Roles = "Admin,Owner")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var ok = await _claims.DeleteAsync(id);
        return ok ? NoContent() : NotFound();
    }

    // POST /claims/{id}/approve — the current-step approver in the claimant's chain.
    [HttpPost("{id}/approve")]
    [Authorize(Roles = "Supervisor,Admin,Owner")]
    public async Task<IActionResult> Approve(string id)
    {
        var result = await _claims.ApproveAsync(id, GetUserId());
        return ToStatusTransitionResponse(result);
    }

    // POST /claims/{id}/reject — the current-step approver in the claimant's chain.
    [HttpPost("{id}/reject")]
    [Authorize(Roles = "Supervisor,Admin,Owner")]
    public async Task<IActionResult> Reject(string id, RejectClaimDto dto)
    {
        var result = await _claims.RejectAsync(id, GetUserId(), dto.ReviewNotes);
        return ToStatusTransitionResponse(result);
    }

    private string GetUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub)
        ?? throw new InvalidOperationException("Authenticated user id is missing.");

    private IActionResult ToStatusTransitionResponse(ClaimStatusTransitionResult result)
    {
        if (!result.Found)
            return NotFound();

        if (!result.Transitioned)
            return BadRequest(new { message = result.ErrorMessage });

        return Ok(result.Claim);
    }
}
