using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltomateHR.Api.Modules.Leave;

// Manual triggers for the scheduled leave jobs — force a run now instead of
// waiting for the background service's next tick. Follows the same shape as
// POST /attendance/cron/auto-clockout/run.
//
// The jobs normally run in-process via LeaveRolloverBackgroundService and
// LeaveAccrualBackgroundService, so no external scheduler or shared secret is
// involved. These endpoints exist for operators and for testing.
//
// Both run SYSTEM-WIDE. The services execute with no request context, so the
// tenant filter is a no-op there; here the caller has a JWT, but the underlying
// service still sweeps every org — deliberate, and the reason this is
// Admin/Owner only.
[ApiController]
[Route("leave/cron")]
[Authorize(Roles = "Admin,Owner")]
public class LeaveCronController : ControllerBase
{
    private readonly ILeaveCronService _cron;
    private readonly ILogger<LeaveCronController> _logger;

    public LeaveCronController(ILeaveCronService cron, ILogger<LeaveCronController> logger)
    {
        _cron = cron;
        _logger = logger;
    }

    // POST /leave/cron/year-rollover?year=YYYY — force-open the target year.
    // `year` defaults to the current UTC year; pass it explicitly to re-open a
    // past year or to pre-open the next one. Safe to re-run: existing rows are
    // skipped, never duplicated (the DB unique index backs that up).
    [HttpPost("year-rollover")]
    public async Task<IActionResult> YearRollover([FromQuery, Range(2000, 2100)] int? year)
    {
        try
        {
            var target = year ?? DateTime.UtcNow.Year;
            return Ok(await _cron.RunYearRolloverAsync(target, DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Leave year rollover failed.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { ok = false, error = "rollover failed", details = ex.Message });
        }
    }

    // POST /leave/cron/monthly-accrual — force a monthly accrual + expiry sweep.
    [HttpPost("monthly-accrual")]
    public async Task<IActionResult> MonthlyAccrual()
    {
        try
        {
            return Ok(await _cron.RunMonthlyAccrualAsync(DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            // A cron has no user to show an error to — log it, and give the
            // scheduler a non-2xx so a failed run is visible in its history.
            _logger.LogError(ex, "Monthly leave accrual failed.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { ok = false, error = "accrual failed", details = ex.Message });
        }
    }
}
