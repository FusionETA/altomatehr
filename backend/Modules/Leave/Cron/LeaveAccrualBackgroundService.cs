namespace AltomateHR.Api.Modules.Leave.Cron;

// Runs the monthly accrual + carry-expiry sweep in-process, following the same
// shape as AutoClockOutBackgroundService: a PeriodicTimer, one scope per tick,
// and a try/catch so a bad pass can't kill the loop.
//
// The difference from the attendance sweeps is the CADENCE. Those want "every
// 15 minutes"; accrual wants "once, on the 1st of each month". A PeriodicTimer
// can't say that, so the timer ticks daily and the work is gated on the date —
// and on a marker of the last month actually run, so a restart on the 1st
// doesn't accrue twice.
//
// Runs with no request context, so the tenant filter is a no-op and one pass
// covers every org. That matches DbSeeder's startup behaviour, not a new pattern.
public class LeaveAccrualBackgroundService : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromHours(6);

    // Malaysian time, matching LeaveCronService: a midnight-MYT firing on the
    // 1st must count as the 1st, and UTC would still read the previous day.
    private static readonly TimeZoneInfo Myt =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Kuala_Lumpur");

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LeaveAccrualBackgroundService> _logger;

    // (year, month) of the last accrual this process ran. Guards a restart on
    // the 1st; the accrual itself is NOT idempotent — running it twice accrues
    // twice, unlike the attendance sweeps.
    private (int Year, int Month)? _lastRun;

    public LeaveAccrualBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<LeaveAccrualBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TickInterval);
        do
        {
            try
            {
                var nowMyt = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Myt);
                if (nowMyt.Day != 1) continue;
                if (_lastRun == (nowMyt.Year, nowMyt.Month)) continue;

                using var scope = _scopeFactory.CreateScope();
                var cron = scope.ServiceProvider.GetRequiredService<ILeaveCronService>();
                var result = await cron.RunMonthlyAccrualAsync(DateTime.UtcNow);

                _lastRun = (nowMyt.Year, nowMyt.Month);
                _logger.LogInformation(
                    "Leave monthly accrual: accrued {Accrued}, carry expired {Expired}, year {Year}.",
                    result.AccruedCount, result.ExpiredCount, result.Year);
            }
            catch (Exception ex)
            {
                // Leave _lastRun unset so the next tick retries today.
                _logger.LogError(ex, "Leave monthly accrual failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
