namespace AltomateHR.Api.Modules.Activity.Cron;

// Deletes sessions and events past the retention window (ActivityOptions),
// shortly after startup and then daily — same shape as
// ApiRequestLogCleanupBackgroundService.
public class ActivityCleanupBackgroundService : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(7);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ActivityCleanupBackgroundService> _logger;

    public ActivityCleanupBackgroundService(
        IServiceScopeFactory scopeFactory, ILogger<ActivityCleanupBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
                var activity = scope.ServiceProvider.GetRequiredService<IActivityService>();
                var deleted = await activity.PurgeExpiredAsync();
                if (deleted > 0)
                    _logger.LogInformation("Activity cleanup: deleted {Deleted} expired rows.", deleted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Activity cleanup failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
