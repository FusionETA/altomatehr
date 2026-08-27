using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using AltomateHR.Api.Modules.Overtime.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AltomateHR.Api.Modules.ApiKeys;

namespace AltomateHR.Api.Modules.Overtime;

[ApiController]
[Route("overtime")]
[Authorize]
public class OvertimeController : ControllerBase
{
    private readonly IOvertimeService _overtime;
    private readonly IOtRateService _rates;

    public OvertimeController(IOvertimeService overtime, IOtRateService rates)
    {
        _overtime = overtime;
        _rates = rates;
    }

    // GET /overtime/rate?date=&projectId= — which OT multiplier applies to the
    // caller on that date, and why. Rate resolution only: computing actual pay
    // needs an hourly rate, which lands with the payroll pass.
    [RequireScope("overtime:read")]
    [HttpGet("rate")]
    public async Task<IActionResult> GetRate([FromQuery] DateTime date, [FromQuery] string? projectId) =>
        Ok(await _rates.ResolveAsync(GetUserId(), date, projectId));

    [RequireScope("overtime:read")]
    [HttpGet]
    public async Task<IActionResult> GetMine() =>
        Ok(await _overtime.GetMineAsync(GetUserId()));

    [RequireScope("overtime:read")]
    [HttpGet("team")]
    [Authorize(Roles = "Supervisor,Admin,Owner")]
    public async Task<IActionResult> GetTeam() =>
        Ok(await _overtime.GetTeamAsync(GetUserId()));

    [RequireScope("overtime:read")]
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var request = await _overtime.GetVisibleByIdAsync(id, GetUserId(), User.IsInRole("Admin"));
        return request is null ? NotFound() : Ok(request);
    }

    [HttpPost]
    public async Task<IActionResult> Submit(CreateOvertimeRequestDto dto)
    {
        var result = await _overtime.SubmitAsync(dto, GetUserId());
        return result.Ok ? Ok(result.Request) : BadRequest(new { message = result.Error });
    }

    [HttpPost("{id}/after-photo")]
    public async Task<IActionResult> AttachAfterPhoto(string id, AttachOvertimeAfterPhotoDto dto) =>
        ToTransitionResponse(await _overtime.AttachAfterPhotoAsync(id, GetUserId(), dto));

    [HttpPost("{id}/approve")]
    [Authorize(Roles = "Supervisor,Admin,Owner")]
    public async Task<IActionResult> Approve(string id) =>
        ToTransitionResponse(await _overtime.ApproveAsync(id, GetUserId()));

    [HttpPost("{id}/reject")]
    [Authorize(Roles = "Supervisor,Admin,Owner")]
    public async Task<IActionResult> Reject(string id, RejectOvertimeDto dto) =>
        ToTransitionResponse(await _overtime.RejectAsync(id, GetUserId(), dto.ReviewNotes));

    [HttpPost("{id}/cancel")]
    public async Task<IActionResult> Cancel(string id) =>
        ToTransitionResponse(await _overtime.CancelAsync(id, GetUserId()));

    [HttpPost("photo")]
    [RequestSizeLimit(8 * 1024 * 1024)]
    public async Task<IActionResult> UploadPhoto(IFormFile? photo)
    {
        if (photo is null || photo.Length == 0)
            return BadRequest(new { message = "Pick a photo to upload." });

        try
        {
            await using var stream = photo.OpenReadStream();
            var result = await _overtime.StorePhotoAsync(
                new OvertimePhotoUpload(photo.FileName, photo.ContentType, photo.Length, stream));
            return Ok(new { photoUrl = result.PhotoUrl });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [RequireScope("overtime:read")]
    [HttpGet("photos/{fileName}")]
    public async Task<IActionResult> GetPhoto(string fileName)
    {
        var photo = await _overtime.GetPhotoForUserAsync(fileName, GetUserId(), User.IsInRole("Admin"));
        if (photo is null)
            return NotFound();

        Response.Headers.CacheControl = "no-store";
        return PhysicalFile(photo.Path, photo.ContentType, photo.DownloadName);
    }

    private IActionResult ToTransitionResponse(OvertimeTransitionResult result)
    {
        if (!result.Found) return NotFound();
        if (!result.Transitioned) return BadRequest(new { message = result.Error });
        return Ok(result.Request);
    }

    private string GetUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub)
        ?? throw new InvalidOperationException("Authenticated user id is missing.");
}
