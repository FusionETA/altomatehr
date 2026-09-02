using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using AltomateHR.Api.Common.Tabular;
using AltomateHR.Api.Modules.Claims.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.ApiKeys;
using AltomateHR.Api.Modules.Ai;
using AltomateHR.Api.Modules.Ai.Dtos;
using Microsoft.AspNetCore.RateLimiting;

namespace AltomateHR.Api.Modules.Claims;

[ApiController]
[Route("[controller]")]        // → /claims
[Authorize]                    // every endpoint here now requires a valid JWT
[RequireModule("claims")]
public class ClaimsController : ControllerBase
{
    private readonly IClaimsService _claims;
    private readonly IReceiptOcrService _ocr;

    public ClaimsController(IClaimsService claims, IReceiptOcrService ocr)
    {
        _claims = claims;
        _ocr = ocr;
    }

    // GET /claims — the caller's own claims.
    [RequireScope("claims:read")]
    [HttpGet]
    public async Task<IActionResult> GetMine() =>
        Ok(await _claims.GetMineAsync(GetUserId()));

    // GET /claims/team — claims awaiting the caller as the current-step approver.
    [RequireScope("claims:read")]
    [HttpGet("team")]
    [Authorize(Roles = "Supervisor,Admin,Owner")]
    public async Task<IActionResult> GetTeam() =>
        Ok(await _claims.GetTeamAsync(GetUserId()));

    // GET /claims/{id}
    [RequireScope("claims:read")]
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

    // POST /claims/receipts/analyze — upload a receipt AND read it with OCR.
    //
    // Stores the file exactly as POST /claims/receipts does, then runs the
    // extraction over the stored copy and returns both. Writes nothing to the
    // database: the values are form pre-fill, and the claim is still created by
    // the normal POST /claims with the returned ReceiptUrl.
    //
    // A failed extraction is NOT a failed upload — the file is already stored, so
    // the response still carries the ReceiptUrl and the client can fall back to
    // manual entry rather than making the user upload again.
    [HttpPost("receipts/analyze")]
    [RequestSizeLimit(8 * 1024 * 1024)]
    [EnableRateLimiting("ocr")]
    public async Task<ActionResult<AnalyzeReceiptResponseDto>> AnalyzeReceipt(
        IFormFile? receiptFile,
        CancellationToken cancellationToken)
    {
        if (receiptFile is null || receiptFile.Length == 0)
            return BadRequest(new { message = "Pick a receipt file to upload." });

        // Buffer once (capped at 8 MB by RequestSizeLimit): the bytes are needed
        // twice — to store, and to analyze.
        byte[] bytes;
        await using (var stream = receiptFile.OpenReadStream())
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            bytes = buffer.ToArray();
        }

        string receiptUrl;
        try
        {
            using var storeStream = new MemoryStream(bytes, writable: false);
            var stored = await _claims.StoreReceiptAsync(new ClaimReceiptUpload(
                receiptFile.FileName,
                receiptFile.ContentType,
                receiptFile.Length,
                storeStream));
            receiptUrl = stored.ReceiptUrl;
        }
        catch (ArgumentException ex)
        {
            // Size/MIME rejection from storage.
            return BadRequest(new { message = ex.Message });
        }

        try
        {
            var extraction = await _ocr.AnalyzeAsync(bytes, receiptFile.ContentType, cancellationToken);
            return Ok(new AnalyzeReceiptResponseDto { ReceiptUrl = receiptUrl, Extraction = extraction });
        }
        catch (AiConfigurationException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = ex.Message, receiptUrl });
        }
        catch (AiProviderException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = ex.Message, receiptUrl });
        }
    }

    // GET /claims/receipts/{fileName}
    [RequireScope("claims:read")]
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

    // GET /claims/export/summary?format=csv|xlsx|pdf&from=&to=&dateField=&status=&employeeId=&projectId=
    // The claims summary as a spreadsheet. Admin/Owner only: it spans the whole
    // org, which is more than a supervisor's own team.
    //
    // Declared BEFORE the {id} route so the literal "export" segment isn't read
    // as a claim id. (ASP.NET prefers literals anyway; keeping them adjacent
    // makes the precedence visible.)
    [RequireScope("claims:read")]
    [HttpGet("export/summary")]
    [Authorize(Roles = "Admin,Owner")]
    public async Task<IActionResult> ExportSummary(
        [FromQuery] ClaimsExportQueryDto query,
        [FromQuery] string? format)
    {
        var result = await _claims.ExportSummaryAsync(query, TabularFormats.Parse(format));

        // Claims change constantly; never let a proxy serve a stale export.
        Response.Headers.CacheControl = "no-store";
        return File(result.Content, result.ContentType, result.FileName);
    }

    // GET /claims/import/template?format=csv|xlsx — the blank import template.
    // PDF is refused here on purpose: a template exists to be filled in.
    [HttpGet("import/template")]
    [Authorize(Roles = "Admin,Owner")]
    public IActionResult ImportTemplate([FromQuery] string? format)
    {
        var resolved = TabularFormats.Parse(format);
        if (!resolved.IsImportable())
        {
            return BadRequest(new
            {
                message = "Templates come as .csv or .xlsx — a PDF can't be filled in and uploaded.",
            });
        }

        var result = _claims.BuildImportTemplate(resolved);
        Response.Headers.CacheControl = "no-store";
        return File(result.Content, result.ContentType, result.FileName);
    }

    // POST /claims/import — multipart upload of historical claims (CSV or XLSX).
    //
    // Always 200, even when rows failed: the body is a per-row report, and a 4xx
    // would tell the client "nothing happened" when in fact 98 of 100 rows
    // landed. A genuinely unusable FILE (wrong type, no header) still 400s.
    [HttpPost("import")]
    [Authorize(Roles = "Admin,Owner")]
    [RequestSizeLimit(8 * 1024 * 1024)]
    public async Task<IActionResult> Import(IFormFile? file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "Pick a .csv or .xlsx file to import." });

        var format = TabularFormats.Detect(file.FileName, file.ContentType);
        if (format is null)
            return BadRequest(new { message = "Unsupported file type. Upload a .csv or .xlsx file." });

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer);

        var result = await _claims.ImportAsync(buffer.ToArray(), format.Value);
        return Ok(result);
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
