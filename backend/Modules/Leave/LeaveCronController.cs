using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltomateHR.Api.Modules.Leave;

// Scheduled leave maintenance. Runs SYSTEM-WIDE (every org) — there is no JWT
// and therefore no tenant context, so the global query filter is a no-op here
// by design. Authenticated by shared secret, not by user.
[ApiController]
[Route("leave/cron")]
[AllowAnonymous]
public class LeaveCronController : ControllerBase
{
    private readonly ILeaveCronService _cron;
    private readonly ILogger<LeaveCronController> _logger;

    public LeaveCronController(ILeaveCronService cron, ILogger<LeaveCronController> logger)
    {
        _cron = cron;
        _logger = logger;
    }

    // POST /leave/cron/year-rollover?year=YYYY — opens the target year.
    // `year` defaults to the current UTC year; pass it explicitly to re-open a
    // past year or to pre-open the next one. Safe to re-run: existing rows are
    // skipped, never duplicated (the DB unique index backs that up).
    [RequireCronSecret]
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

    // POST /leave/cron/monthly-accrual — run on the 1st of each month.
    [RequireCronSecret]
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
