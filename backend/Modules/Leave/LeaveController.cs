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
    private readonly ILeaveCronService _cron;
    private readonly ILogger<LeaveController> _logger;

    public LeaveController(
        ILeaveService leave, ILeaveCronService cron, ILogger<LeaveController> logger)
    {
        _leave = leave;
        _cron = cron;
        _logger = logger;
    }

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

    // PUT /leave/entitlements/{employeeId}/{leaveTypeId}?year=YYYY
    // Override one employee's entitlement. Admin/Owner: it grants days.
    [HttpPut("entitlements/{employeeId}/{leaveTypeId}")]
    [Authorize(Roles = "Admin,Owner")]
    public async Task<IActionResult> SetEntitlement(
        string employeeId, string leaveTypeId,
        [FromQuery, Range(2000, 2100)] int? year,
        SetEntitlementDto dto) =>
        ToEntitlementResponse(await _leave.SetEntitlementAsync(
            employeeId, leaveTypeId, year ?? DateTime.UtcNow.Year, dto));

    // POST /leave/entitlements/{employeeId}/{leaveTypeId}/reset?year=YYYY
    [HttpPost("entitlements/{employeeId}/{leaveTypeId}/reset")]
    [Authorize(Roles = "Admin,Owner")]
    public async Task<IActionResult> ResetEntitlement(
        string employeeId, string leaveTypeId,
        [FromQuery, Range(2000, 2100)] int? year) =>
        ToEntitlementResponse(await _leave.ResetEntitlementAsync(
            employeeId, leaveTypeId, year ?? DateTime.UtcNow.Year));

    // POST /leave/entitlements/{employeeId}/seed?year=YYYY — opens the year
    // for someone who joined after the rollover ran.
    [HttpPost("entitlements/{employeeId}/seed")]
    [Authorize(Roles = "Admin,Owner")]
    public async Task<IActionResult> SeedEntitlements(
        string employeeId, [FromQuery, Range(2000, 2100)] int? year) =>
        Ok(new { created = await _leave.SeedEntitlementsAsync(
            employeeId, year ?? DateTime.UtcNow.Year) });

    // GET /leave/overview?year=YYYY — admin dashboard summary.
    [RequireScope("leave:read")]
    [HttpGet("overview")]
    [Authorize(Roles = "Admin,Owner")]
    public async Task<IActionResult> Overview([FromQuery, Range(2000, 2100)] int? year) =>
        Ok(await _leave.GetOverviewAsync(year ?? DateTime.UtcNow.Year));

    // GET /leave/approved-days?employeeId=&from=&to= — days taken in a range.
    [RequireScope("leave:read")]
    [HttpGet("approved-days")]
    [Authorize(Roles = "Supervisor,Admin,Owner")]
    public async Task<IActionResult> ApprovedDays(
        [FromQuery, Required] string employeeId,
        [FromQuery, Required] DateTime from,
        [FromQuery, Required] DateTime to) =>
        Ok(new { employeeId, from, to, days = await _leave.GetApprovedDaysInRangeAsync(employeeId, from, to) });

    // Ok=false with no Error means "not in this org" (404); with an Error it's
    // a rule refusal (400).
    private IActionResult ToEntitlementResponse(LeaveEntitlementResult r)
    {
        if (r.Ok) return Ok(r.Balance);
        return r.Error is null ? NotFound(new { message = "Employee not found." })
                               : BadRequest(new { message = r.Error });
    }

    // GET /leave/team/balances?year=YYYY — balances for the caller's reports.
    [RequireScope("leave:read")]
    [HttpGet("team/balances")]
    [Authorize(Roles = "Supervisor,Admin,Owner")]
    public async Task<IActionResult> TeamBalances([FromQuery, Range(2000, 2100)] int? year)
    {
        var resolved = year ?? DateTime.UtcNow.Year;
        var rows = await _leave.GetTeamBalancesAsync(GetUserId(), resolved);
        return Ok(new { data = rows, total = rows.Count(), year = resolved });
    }

    // GET /leave/on-leave-today?date=YYYY-MM-DD — who is out (approved) today.
    [RequireScope("leave:read")]
    [HttpGet("on-leave-today")]
    [Authorize(Roles = "Supervisor,Admin,Owner")]
    public async Task<IActionResult> OnLeaveToday([FromQuery] DateTime? date) =>
        Ok(await _leave.GetOnLeaveTodayAsync(date ?? DateTime.UtcNow));

    // GET /leave/pending-count — badge count for the caller's approval queue.
    [RequireScope("leave:read")]
    [HttpGet("pending-count")]
    [Authorize(Roles = "Supervisor,Admin,Owner")]
    public async Task<IActionResult> PendingCount() =>
        Ok(new { pendingLeaveApprovals = await _leave.CountPendingApprovalsAsync(GetUserId()) });

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

    // GET /leave/summary-report?employeeId=&year=YYYY — the yearly summary as
    // JSON: the month-by-month matrix plus the approved-request detail. Same
    // data the PDF renders, for a frontend that would rather draw it itself.
    [RequireScope("leave:read")]
    [HttpGet("summary-report")]
    public async Task<IActionResult> SummaryReport(
        [FromQuery, Required] string employeeId,
        [FromQuery, Range(2000, 2100)] int? year)
    {
        var result = await _leave.GetSummaryReportAsync(employeeId, year ?? DateTime.UtcNow.Year);
        if (!result.Found) return NotFound(new { message = "Employee not found." });
        if (!result.Allowed) return Forbid();
        return Ok(result.Report);
    }

    // GET /leave/export/summary.pdf?employeeId=&year=YYYY — production's
    // two-page A4 landscape summary.
    [RequireScope("leave:read")]
    [HttpGet("export/summary.pdf")]
    public async Task<IActionResult> ExportSummaryPdf(
        [FromQuery, Required] string employeeId,
        [FromQuery, Range(2000, 2100)] int? year)
    {
        var result = await _leave.ExportSummaryPdfAsync(employeeId, year ?? DateTime.UtcNow.Year);
        if (!result.Found) return NotFound(new { message = "Employee not found." });
        if (!result.Allowed) return Forbid();

        Response.Headers.CacheControl = "no-store";
        return File(result.Content, "application/pdf", result.FileName);
    }

    // GET /leave/export/summary-bulk.zip?year=YYYY&employeeIds=a&employeeIds=b
    // One PDF per employee, zipped. Omit employeeIds for the whole org.
    [RequireScope("leave:read")]
    [HttpGet("export/summary-bulk.zip")]
    [Authorize(Roles = "Admin,Owner")]
    public async Task<IActionResult> ExportBulkSummaryZip(
        [FromQuery, Range(2000, 2100)] int? year,
        [FromQuery] string[]? employeeIds)
    {
        var result = await _leave.ExportBulkSummaryZipAsync(
            year ?? DateTime.UtcNow.Year, employeeIds);

        if (!result.Found) return NotFound(new { message = "No employees found." });

        Response.Headers.CacheControl = "no-store";
        return File(result.Content, "application/zip", result.FileName);
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

    // PUT /leave/{id} — edit your own pending request.
    [HttpPut("{id}")]
    public async Task<IActionResult> Edit(string id, CreateLeaveApplicationDto dto)
    {
        var result = await _leave.EditAsync(id, dto, GetUserId());
        if (result.Ok) return Ok(result.Application);
        return result.Error == "Application not found"
            ? NotFound(new { message = result.Error })
            : BadRequest(new { message = result.Error });
    }

    // POST /leave/on-behalf/{employeeId} — an admin files leave for someone.
    // Lands APPROVED and records who did it.
    [HttpPost("on-behalf/{employeeId}")]
    [Authorize(Roles = "Admin,Owner")]
    public async Task<IActionResult> ApplyOnBehalf(string employeeId, CreateLeaveApplicationDto dto)
    {
        var result = await _leave.ApplyOnBehalfAsync(employeeId, dto, GetUserId());
        if (result.Ok) return Ok(result.Application);
        return result.Error is null
            ? NotFound(new { message = "Employee not found." })
            : BadRequest(new { message = result.Error });
    }

    // GET /leave/{id}/audit — the decision trail for one request.
    [RequireScope("leave:read")]
    [HttpGet("{id}/audit")]
    public async Task<IActionResult> Audit(string id)
    {
        var result = await _leave.GetAuditTrailAsync(id);
        if (!result.Found) return NotFound(new { message = "Application not found" });
        if (!result.Allowed) return Forbid();
        return Ok(result.Entries);
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

    // ---- Scheduled-job triggers -------------------------------------------
    // Force a run now instead of waiting for the background service's next
    // tick. The jobs normally run in-process via LeaveRolloverBackgroundService
    // and LeaveAccrualBackgroundService, so no external scheduler or shared
    // secret is involved; these exist for operators and for testing. Same shape
    // as POST /attendance/cron/auto-clockout/run, which likewise lives on its
    // own module's controller rather than a separate cron controller.
    //
    // Both run SYSTEM-WIDE. The services execute with no request context, so
    // the tenant filter is a no-op there; here the caller has a JWT, but the
    // underlying service still sweeps every org — deliberate, and the reason
    // these are Admin/Owner only.

    // POST /leave/cron/year-rollover?year=YYYY — force-open the target year.
    // `year` defaults to the current UTC year; pass it explicitly to re-open a
    // past year or to pre-open the next one. Safe to re-run: existing rows are
    // skipped, never duplicated (the DB unique index backs that up).
    [HttpPost("cron/year-rollover")]
    [Authorize(Roles = "Admin,Owner")]
    public async Task<IActionResult> YearRollover([FromQuery, Range(2000, 2100)] int? year)
    {
        try
        {
            var target = year ?? DateTime.UtcNow.Year;
            return Ok(await _cron.RunYearRolloverAsync(target, DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            // Log the detail; don't hand the caller the exception message.
            _logger.LogError(ex, "Leave year rollover failed.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { ok = false, error = "rollover failed" });
        }
    }

    // POST /leave/cron/monthly-accrual — force a monthly accrual + expiry sweep.
    [HttpPost("cron/monthly-accrual")]
    [Authorize(Roles = "Admin,Owner")]
    public async Task<IActionResult> MonthlyAccrual()
    {
        try
        {
            return Ok(await _cron.RunMonthlyAccrualAsync(DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            // A cron has no user to show an error to — log it, and give the
            // caller a non-2xx so a failed run is visible.
            _logger.LogError(ex, "Monthly leave accrual failed.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { ok = false, error = "accrual failed" });
        }
    }

    private IActionResult ToTransitionResponse(LeaveTransitionResult result)
    {
        if (!result.Found) return NotFound(new { message = result.Error ?? "Application not found" });
        if (result.Transitioned) return Ok(result.Application);

        // Authorization refusals are 403; everything else is a state/rule 400.
        return result.Error is "You are not authorized to review this step"
                            or "Only the applicant can cancel"
            ? StatusCode(StatusCodes.Status403Forbidden, new { message = result.Error })
            : BadRequest(new { message = result.Error });
    }

    private string GetUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub)
        ?? throw new InvalidOperationException("Authenticated user id is missing.");
}
