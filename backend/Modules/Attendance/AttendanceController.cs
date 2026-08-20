using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using AltomateHR.Api.Modules.Attendance.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.ApiKeys;

namespace AltomateHR.Api.Modules.Attendance;

[ApiController]
[Route("[controller]")]        // → /attendance
[Authorize]
[RequireModule("attendance")]
public class AttendanceController : ControllerBase
{
    private readonly IAttendanceService _attendance;

    public AttendanceController(IAttendanceService attendance) => _attendance = attendance;

    // GET /attendance — history. Admins see the whole org (roll call);
    // employees see only their own records.
    [RequireScope("attendance:read")]
    [HttpGet]
    public async Task<IActionResult> GetAll() =>
        Ok(await _attendance.GetHistoryAsync(GetUserId(), User.IsInRole("Admin")));

    // GET /attendance/team — records awaiting the caller as current-step approver.
    [RequireScope("attendance:read")]
    [HttpGet("team")]
    [Authorize(Roles = "Supervisor,Admin,Owner")]
    public async Task<IActionResult> GetTeamApprovals() =>
        Ok(await _attendance.GetTeamApprovalsAsync(GetUserId()));

    // GET /attendance/today — the caller's record for the current local day (204 if none yet).
    [RequireScope("attendance:read")]
    [HttpGet("today")]
    public async Task<IActionResult> GetToday()
    {
        var record = await _attendance.GetTodayAsync(GetUserId());
        return record is null ? NoContent() : Ok(record);
    }

    // POST /attendance/clock-in
    [HttpPost("clock-in")]
    public async Task<IActionResult> ClockIn(ClockInDto dto) =>
        ToResponse(await _attendance.ClockInAsync(GetUserId(), dto));

    // POST /attendance/clock-out
    [HttpPost("clock-out")]
    public async Task<IActionResult> ClockOut(ClockOutDto dto) =>
        ToResponse(await _attendance.ClockOutAsync(GetUserId(), dto));

    // POST /attendance/{id}/approve — current-step approver only.
    [HttpPost("{id}/approve")]
    [Authorize(Roles = "Supervisor,Admin,Owner")]
    public async Task<IActionResult> Approve(string id) =>
        ToTransitionResponse(await _attendance.ApproveAsync(id, GetUserId()));

    // POST /attendance/{id}/reject — current-step approver only.
    [HttpPost("{id}/reject")]
    [Authorize(Roles = "Supervisor,Admin,Owner")]
    public async Task<IActionResult> Reject(string id, RejectAttendanceDto dto) =>
        ToTransitionResponse(await _attendance.RejectAsync(id, GetUserId(), dto.ReviewNotes));

    // POST /attendance/photo — off-site proof photo. Returns { photoUrl } to
    // include in the clock-in/out request.
    [HttpPost("photo")]
    [RequestSizeLimit(8 * 1024 * 1024)]
    public async Task<IActionResult> UploadPhoto(IFormFile? photo)
    {
        if (photo is null || photo.Length == 0)
            return BadRequest(new { message = "Pick a photo to upload." });

        try
        {
            await using var stream = photo.OpenReadStream();
            var result = await _attendance.StorePhotoAsync(
                new AttendancePhotoUpload(photo.FileName, photo.ContentType, photo.Length, stream));
            return Ok(new { photoUrl = result.PhotoUrl });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // GET /attendance/photos/{fileName} — serve a photo (owner or admin only).
    [RequireScope("attendance:read")]
    [HttpGet("photos/{fileName}")]
    public async Task<IActionResult> GetPhoto(string fileName)
    {
        var photo = await _attendance.GetPhotoForUserAsync(fileName, GetUserId(), User.IsInRole("Admin"));
        if (photo is null)
            return NotFound();

        Response.Headers.CacheControl = "no-store";
        return PhysicalFile(photo.Path, photo.ContentType, photo.DownloadName);
    }

    private IActionResult ToResponse(AttendanceActionResult result) =>
        result.Ok
            ? Ok(result.Record)
            : BadRequest(new { message = result.Error, code = result.Code, distanceMeters = result.DistanceMeters });

    private IActionResult ToTransitionResponse(AttendanceTransitionResult result)
    {
        if (!result.Found) return NotFound();
        if (!result.Transitioned) return BadRequest(new { message = result.Error });
        return Ok(result.Record);
    }

    private string GetUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub)
        ?? throw new InvalidOperationException("Authenticated user id is missing.");
}
