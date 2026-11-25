using AltomateHR.Api.Modules.Activity.Entities;

namespace AltomateHR.Api.Modules.Activity.Cron;

// Saves what UserActivityMiddleware queued, in batches — the same loop as
// ApiRequestLogWriterBackgroundService: one INSERT round-trip per batch, none
// of it on the request path. A batch that fails is logged and dropped; these
// rows are usage insight, not records anyone is owed.
public class ActivityEventWriterBackgroundService : BackgroundService
{
    private const int MaxBatch = 500;
    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(2);

    private readonly IActivityEventQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ActivityEventWriterBackgroundService> _logger;

    public ActivityEventWriterBackgroundService(
        IActivityEventQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<ActivityEventWriterBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<UserActivityEvent>(MaxBatch);
        try
        {
            while (await _queue.Reader.WaitToReadAsync(stoppingToken))
            {
                // Give a burst a moment to fill the batch, then save what arrived.
                var deadline = DateTime.UtcNow + MaxWait;
                while (batch.Count < MaxBatch)
                {
                    if (_queue.Reader.TryRead(out var row))
                    {
                        batch.Add(row);
                        continue;
                    }

                    var remaining = deadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero) break;

                    using var wait = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    wait.CancelAfter(remaining);
                    try
                    {
                        if (!await _queue.Reader.WaitToReadAsync(wait.Token)) break;
                    }
                    catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                    {
                        break;   // the wait timed out, not the app
                    }
                }

                await SaveAsync(batch);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down — fall through and flush.
        }

        while (_queue.Reader.TryRead(out var row))
        {
            batch.Add(row);
            if (batch.Count >= MaxBatch) await SaveAsync(batch);
        }
        await SaveAsync(batch);
    }

    private async Task SaveAsync(List<UserActivityEvent> batch)
    {
        if (batch.Count == 0) return;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var activity = scope.ServiceProvider.GetRequiredService<IActivityService>();
            await activity.RecordBatchAsync(batch.ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save {Count} activity events; dropping them.", batch.Count);
        }
        finally
        {
            batch.Clear();
        }
    }
}
