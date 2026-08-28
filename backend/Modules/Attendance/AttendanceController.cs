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
    private readonly IHoursSummaryService _hoursSummary;

    public AttendanceController(IAttendanceService attendance, IHoursSummaryService hoursSummary)
    {
        _attendance = attendance;
        _hoursSummary = hoursSummary;
    }

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

    // POST /attendance/cron/auto-clockout/run — force-run the auto-clockout
    // sweep now, instead of waiting for the background service's next tick.
    // Same underlying logic AutoClockOutBackgroundService calls on its timer.
    // The cutoff is no longer a parameter — it comes from each employee's
    // policy (AutoClockOutEnabled + AutoClockOutAfterMinutes).
    [HttpPost("cron/auto-clockout/run")]
    [Authorize(Roles = "Admin,Owner")]
    public async Task<IActionResult> RunAutoClockOutSweep([FromQuery] int? maxCandidates) =>
        Ok(await _attendance.RunAutoClockOutSweepAsync(maxCandidates ?? 200));

    // GET /attendance/warnings/still-clocked-in — employees clocked in longer
    // than thresholdMinutes (default 600 = 10h). Detection only — no
    // notification is sent; the AutoClockOutBackgroundService logs the same
    // detection on a timer, this is the on-demand equivalent. Admin/Owner only.
    [RequireScope("attendance:read")]
    [HttpGet("warnings/still-clocked-in")]
    [Authorize(Roles = "Admin,Owner")]
    public async Task<IActionResult> GetStillClockedInWarnings([FromQuery] int? thresholdMinutes) =>
        Ok(await _attendance.GetStillClockedInWarningsAsync(thresholdMinutes ?? 600));

    // GET /attendance/pending-approvals/digest — the caller's own pending-
    // approval count. Detection only, same caveat as above.
    [RequireScope("attendance:read")]
    [HttpGet("pending-approvals/digest")]
    [Authorize(Roles = "Supervisor,Admin,Owner")]
    public async Task<IActionResult> GetPendingApprovalDigest() =>
        Ok(await _attendance.GetPendingApprovalDigestAsync(GetUserId()));

    // GET /attendance/hours-summary/me — the caller's own worked-minutes totals for a range.
    [RequireScope("attendance:read")]
    [HttpGet("hours-summary/me")]
    public async Task<IActionResult> GetMyHoursSummary([FromQuery] DateTime from, [FromQuery] DateTime to) =>
        Ok(await _hoursSummary.GetMyHoursSummaryAsync(GetUserId(), from, to));

    // GET /attendance/hours-summary/org — org-wide totals, one row per Employee/
    // Supervisor, optionally narrowed to one team. Admin/Owner only.
    [RequireScope("attendance:read")]
    [HttpGet("hours-summary/org")]
    [Authorize(Roles = "Admin,Owner")]
    public async Task<IActionResult> GetOrgHoursSummary(
        [FromQuery] DateTime from, [FromQuery] DateTime to, [FromQuery] string? teamId) =>
        Ok(await _hoursSummary.GetOrgHoursSummaryAsync(from, to, teamId));

    // GET /attendance/hours-summary/employees/{employeeId} — one employee's totals,
    // for the employee themself or their approver (supervisor/admin/owner).
    [RequireScope("attendance:read")]
    [HttpGet("hours-summary/employees/{employeeId}")]
    public async Task<IActionResult> GetEmployeeHoursSummary(
        string employeeId, [FromQuery] DateTime from, [FromQuery] DateTime to)
    {
        var result = await _hoursSummary.GetEmployeeHoursSummaryAsync(
            employeeId, from, to, GetUserId(), User.FindFirstValue(ClaimTypes.Role));
        return result is null ? Forbid() : Ok(result);
    }

    // POST /attendance/clock-in
    [HttpPost("clock-in")]
    public async Task<IActionResult> ClockIn(ClockInDto dto) =>
        ToResponse(await _attendance.ClockInAsync(GetUserId(), dto));

    // POST /attendance/clock-out
    [HttpPost("clock-out")]
    public async Task<IActionResult> ClockOut(ClockOutDto dto) =>
        ToResponse(await _attendance.ClockOutAsync(GetUserId(), dto));

    // POST /attendance/adjustments — employee requests a correction to their own
    // clock-in and/or clock-out time. Takes effect on the record only once a
    // supervisor approves it via the normal POST /attendance/{id}/approve flow —
    // rejecting it just leaves the record unchanged.
    [HttpPost("adjustments")]
    public async Task<IActionResult> SubmitAdjustment(SubmitTimeAdjustmentDto dto)
    {
        var result = await _attendance.SubmitTimeAdjustmentAsync(GetUserId(), dto);
        return result.Ok ? Ok(result.Requests) : BadRequest(new { message = result.Error });
    }

    // POST /attendance/{id}/approve — {id} is an AttendanceApprovalRequest id
    // (CLOCK_IN/CLOCK_OUT only). Current-step approver only.
    [HttpPost("{id}/approve")]
    [Authorize(Roles = "Supervisor,Admin,Owner")]
    public async Task<IActionResult> Approve(string id) =>
        ToTransitionResponse(await _attendance.ApproveAsync(id, GetUserId()));

    // POST /attendance/{id}/reject — same id space as Approve above.
    [HttpPost("{id}/reject")]
    [Authorize(Roles = "Supervisor,Admin,Owner")]
    public async Task<IActionResult> Reject(string id, RejectAttendanceDto dto) =>
        ToTransitionResponse(await _attendance.RejectAsync(id, GetUserId(), dto.ReviewNotes));

    // POST /attendance/bulk/approve — approve many approval requests (records
    // and/or breaks) in one call. Independent per-id success/failure.
    [HttpPost("bulk/approve")]
    [Authorize(Roles = "Supervisor,Admin,Owner")]
    public async Task<IActionResult> BulkApprove(BulkApproveDto dto) =>
        Ok(await _attendance.BulkApproveAsync(dto.Ids, GetUserId()));

    // POST /attendance/bulk/reject
    [HttpPost("bulk/reject")]
    [Authorize(Roles = "Supervisor,Admin,Owner")]
    public async Task<IActionResult> BulkReject(BulkRejectDto dto) =>
        Ok(await _attendance.BulkRejectAsync(dto.Ids, GetUserId(), dto.ReviewNotes));

    // GET /attendance/audit-log — every approval decision (any kind), for compliance review.
    [RequireScope("attendance:read")]
    [HttpGet("audit-log")]
    [Authorize(Roles = "Admin,Owner")]
    public async Task<IActionResult> GetAuditLog([FromQuery] string? employeeId, [FromQuery] DateTime? from, [FromQuery] DateTime? to) =>
        Ok(await _attendance.GetAuditLogAsync(employeeId, from, to));

    // POST /attendance/break/start — start a break on today's active session.
    [HttpPost("break/start")]
    public async Task<IActionResult> StartBreak(StartBreakDto dto) =>
        ToBreakResponse(await _attendance.StartBreakAsync(GetUserId(), dto));

    // POST /attendance/break/end — end the currently-open break.
    [HttpPost("break/end")]
    public async Task<IActionResult> EndBreak(EndBreakDto dto) =>
        ToBreakResponse(await _attendance.EndBreakAsync(GetUserId(), dto));

    // POST /attendance/break/{id}/approve — {id} is an AttendanceApprovalRequest
    // id (BREAK_START/BREAK_END only). Current-step approver only.
    [HttpPost("break/{id}/approve")]
    [Authorize(Roles = "Supervisor,Admin,Owner")]
    public async Task<IActionResult> ApproveBreak(string id) =>
        ToBreakTransitionResponse(await _attendance.ApproveBreakAsync(id, GetUserId()));

    // POST /attendance/break/{id}/reject — current-step approver only.
    [HttpPost("break/{id}/reject")]
    [Authorize(Roles = "Supervisor,Admin,Owner")]
    public async Task<IActionResult> RejectBreak(string id, RejectBreakDto dto) =>
        ToBreakTransitionResponse(await _attendance.RejectBreakAsync(id, GetUserId(), dto.ReviewNotes));

    // GET /attendance/team/breaks — breaks awaiting the caller as current-step approver.
    [RequireScope("attendance:read")]
    [HttpGet("team/breaks")]
    [Authorize(Roles = "Supervisor,Admin,Owner")]
    public async Task<IActionResult> GetTeamBreakApprovals() =>
        Ok(await _attendance.GetTeamBreakApprovalsAsync(GetUserId()));

    // GET /attendance/{recordId}/breaks — every break for that day's record.
    // Self-access (the record's own employee) or anyone who can approve for them.
    [RequireScope("attendance:read")]
    [HttpGet("{recordId}/breaks")]
    public async Task<IActionResult> GetBreaks(string recordId) =>
        ToBreakListResponse(await _attendance.GetBreaksForRecordAsync(
            recordId, GetUserId(), User.FindFirstValue(ClaimTypes.Role)));

    // GET /attendance/selfies/stats — how many selfies are stored and their date range.
    [RequireScope("attendance:read")]
    [HttpGet("selfies/stats")]
    [Authorize(Roles = "Admin,Owner")]
    public async Task<IActionResult> GetSelfieStorageStats() =>
        Ok(await _attendance.GetSelfieStorageStatsAsync());

    // POST /attendance/selfies/delete-range — bulk-delete stored selfies within
    // an inclusive date range and clear the record's photo URL(s).
    [HttpPost("selfies/delete-range")]
    [Authorize(Roles = "Admin,Owner")]
    public async Task<IActionResult> DeleteSelfiesInRange(DeleteSelfiesInRangeDto dto)
    {
        if (dto.From > dto.To)
            return BadRequest(new { message = "Start date must be on or before end date." });

        return Ok(await _attendance.DeleteSelfiesInRangeAsync(dto.From, dto.To));
    }

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

    private IActionResult ToBreakResponse(AttendanceBreakActionResult result) =>
        result.Ok
            ? Ok(result.Break)
            : BadRequest(new { message = result.Error, code = result.Code });

    private IActionResult ToBreakTransitionResponse(AttendanceBreakTransitionResult result)
    {
        if (!result.Found) return NotFound();
        if (!result.Transitioned) return BadRequest(new { message = result.Error });
        return Ok(result.Break);
    }

    private IActionResult ToBreakListResponse(AttendanceBreakListResult result)
    {
        if (!result.Found) return NotFound();
        if (!result.Authorized) return Forbid();
        return Ok(result.Breaks);
    }

    private string GetUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub)
        ?? throw new InvalidOperationException("Authenticated user id is missing.");
}
