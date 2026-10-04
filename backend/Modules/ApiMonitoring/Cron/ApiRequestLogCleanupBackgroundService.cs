namespace AltomateHR.Api.Modules.ApiMonitoring.Cron;

// Deletes request-log rows past the retention window, once shortly after
// startup and then daily — every request adds a row, so without this the table
// only ever grows. Same in-process timer shape as AutoClockOutBackgroundService.
public class ApiRequestLogCleanupBackgroundService : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ApiRequestLogCleanupBackgroundService> _logger;

    public ApiRequestLogCleanupBackgroundService(
        IServiceScopeFactory scopeFactory, ILogger<ApiRequestLogCleanupBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Not at the instant of boot: startup already runs migrations and the
        // other sweeps, and a large first delete has no reason to join them.
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var monitoring = scope.ServiceProvider.GetRequiredService<IApiMonitoringService>();
                var deleted = await monitoring.PurgeExpiredAsync();
                if (deleted > 0)
                    _logger.LogInformation("API request log cleanup: deleted {Deleted} expired rows.", deleted);
            }
            catch (Exception ex)
            {
                // One failed run can't kill the loop — log and try again tomorrow.
                _logger.LogError(ex, "API request log cleanup failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
