namespace AltomateHR.Api.Modules.Leave.Cron;

// Opens the new leave year in-process. Same shape as the accrual service, but
// gated on 1 January rather than the 1st of any month.
//
// Unlike accrual, the rollover IS idempotent — existing rows are skipped, and a
// unique index on (org, employee, type, year) backs that up — so a double run
// is harmless. The last-run marker is kept anyway to avoid pointless work and
// noisy logs.
public class LeaveRolloverBackgroundService : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromHours(6);

    private static readonly TimeZoneInfo Myt =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Kuala_Lumpur");

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LeaveRolloverBackgroundService> _logger;

    private int? _lastRunYear;

    public LeaveRolloverBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<LeaveRolloverBackgroundService> logger)
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
                if (nowMyt is not { Month: 1, Day: 1 }) continue;
                if (_lastRunYear == nowMyt.Year) continue;

                using var scope = _scopeFactory.CreateScope();
                var cron = scope.ServiceProvider.GetRequiredService<ILeaveCronService>();
                var result = await cron.RunYearRolloverAsync(nowMyt.Year, DateTime.UtcNow);

                _lastRunYear = nowMyt.Year;
                _logger.LogInformation(
                    "Leave year rollover {Year}: created {Created}, skipped {Skipped}.",
                    result.Year, result.Created, result.Skipped);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Leave year rollover failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
