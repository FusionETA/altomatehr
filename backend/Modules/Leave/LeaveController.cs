using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using AltomateHR.Api.Modules.Leave.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AltomateHR.Api.Modules.ApiKeys;

namespace AltomateHR.Api.Modules.Leave;

[ApiController]
[Route("leave")]
[Authorize]
public class LeaveController : ControllerBase
{
    private readonly ILeaveService _leave;

    public LeaveController(ILeaveService leave) => _leave = leave;

    // GET /leave — the caller's own applications.
    [RequireScope("leave:read")]
    [HttpGet]
    public async Task<IActionResult> GetMine() =>
        Ok(await _leave.GetMineAsync(GetUserId()));

    // GET /leave/team — applications awaiting the caller as the current-step approver.
    [RequireScope("leave:read")]
    [HttpGet("team")]
    [Authorize(Roles = "Supervisor,Admin,Owner")]
    public async Task<IActionResult> GetTeam() =>
        Ok(await _leave.GetTeamAsync(GetUserId()));

    // GET /leave/balances?year=YYYY — the caller's per-type balances.
    // `year` defaults to the current UTC year.
    [RequireScope("leave:read")]
    [HttpGet("balances")]
    public async Task<IActionResult> Balances([FromQuery, Range(2000, 2100)] int? year) =>
        Ok(await _leave.GetBalancesAsync(GetUserId(), year ?? DateTime.UtcNow.Year));

    // GET /leave/balances/all?year=YYYY — every employee in the org (admin grid).
    // Declared BEFORE the {employeeId} route: literal segments win in ASP.NET
    // routing, but keeping them adjacent makes the precedence obvious.
    [RequireScope("leave:read")]
    [HttpGet("balances/all")]
    [Authorize(Roles = "Admin,Owner")]
    public async Task<IActionResult> OrgBalances([FromQuery, Range(2000, 2100)] int? year)
    {
        var resolved = year ?? DateTime.UtcNow.Year;
        var rows = await _leave.GetOrgBalancesAsync(resolved);
        return Ok(new { data = rows, total = rows.Count(), year = resolved });
    }

    // GET /leave/balances/{employeeId}?year=YYYY — one employee's balances.
    // Admin/Owner → anyone in the org; supervisors → their direct reports;
    // everyone → themselves. 404 when the id isn't in the caller's org.
    [RequireScope("leave:read")]
    [HttpGet("balances/{employeeId}")]
    public async Task<IActionResult> BalancesFor(
        string employeeId,
        [FromQuery, Range(2000, 2100)] int? year)
    {
        var result = await _leave.GetBalancesForEmployeeAsync(
            employeeId, year ?? DateTime.UtcNow.Year);

        if (!result.Found)
            return NotFound(new { message = "Employee not found." });

        if (!result.Allowed)
            return Forbid();

        return Ok(new { data = result.Balances, total = result.Balances.Count(), year = result.Year });
    }

    // GET /leave/export/summary?employeeId=&year=YYYY — balances as a CSV file.
    // Same three-tier access as the JSON reader. Production returns a PDF here;
    // CSV is the V2 stand-in until a renderer is chosen.
    [RequireScope("leave:read")]
    [HttpGet("export/summary")]
    public async Task<IActionResult> ExportSummary(
        [FromQuery, Required] string employeeId,
        [FromQuery, Range(2000, 2100)] int? year)
    {
        var result = await _leave.ExportBalancesCsvAsync(
            employeeId, year ?? DateTime.UtcNow.Year);

        if (!result.Found) return NotFound(new { message = "Employee not found." });
        if (!result.Allowed) return Forbid();

        // Balances change; never let a proxy or browser serve a stale export.
        Response.Headers.CacheControl = "no-store";
        return File(result.Content, "text/csv", result.FileName);
    }

    // GET /leave/export/summary/all?year=YYYY — every employee, one CSV.
    // Declared before the {employeeId}-less sibling for the same literal-vs-
    // parameter reason as the balances routes.
    [RequireScope("leave:read")]
    [HttpGet("export/summary/all")]
    [Authorize(Roles = "Admin,Owner")]
    public async Task<IActionResult> ExportOrgSummary([FromQuery, Range(2000, 2100)] int? year)
    {
        var result = await _leave.ExportOrgBalancesCsvAsync(year ?? DateTime.UtcNow.Year);
        Response.Headers.CacheControl = "no-store";
        return File(result.Content, "text/csv", result.FileName);
    }

    // GET /leave/files/{xeroFileId}/content — proxy a leave attachment.
    // Server-side proxy so the Xero OAuth token never reaches the browser,
    // mirroring the claim-receipt and attendance-photo proxies.
    [RequireScope("leave:read")]
    [HttpGet("files/{xeroFileId}/content")]
    public async Task<IActionResult> Attachment(string xeroFileId)
    {
        var result = await _leave.GetAttachmentAsync(xeroFileId);
        if (!result.Found) return NotFound(new { message = "Not found." });

        Response.Headers.CacheControl = "no-store";
        return File(result.Content, result.ContentType, result.FileName);
    }

    // POST /leave — apply.
    [HttpPost]
    public async Task<IActionResult> Apply(CreateLeaveApplicationDto dto)
    {
        var result = await _leave.ApplyAsync(dto, GetUserId());
        return result.Ok ? Ok(result.Application) : BadRequest(new { message = result.Error });
    }

    // POST /leave/{id}/approve — the current-step approver in the applicant's chain.
    [HttpPost("{id}/approve")]
    [Authorize(Roles = "Supervisor,Admin,Owner")]
    public async Task<IActionResult> Approve(string id) =>
        ToTransitionResponse(await _leave.ApproveAsync(id, GetUserId()));

    // POST /leave/{id}/reject — the current-step approver in the applicant's chain.
    [HttpPost("{id}/reject")]
    [Authorize(Roles = "Supervisor,Admin,Owner")]
    public async Task<IActionResult> Reject(string id, RejectLeaveDto dto) =>
        ToTransitionResponse(await _leave.RejectAsync(id, GetUserId(), dto.ReviewNotes));

    // POST /leave/{id}/cancel — the owner cancels their own pending request.
    [HttpPost("{id}/cancel")]
    public async Task<IActionResult> Cancel(string id) =>
        ToTransitionResponse(await _leave.CancelAsync(id, GetUserId()));

    private IActionResult ToTransitionResponse(LeaveTransitionResult result)
    {
        if (!result.Found) return NotFound();
        if (!result.Transitioned) return BadRequest(new { message = result.Error });
        return Ok(result.Application);
    }

    private string GetUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub)
        ?? throw new InvalidOperationException("Authenticated user id is missing.");
}
