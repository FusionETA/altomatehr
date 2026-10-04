using AltomateHR.Api.Modules.ApiMonitoring.Entities;

namespace AltomateHR.Api.Modules.ApiMonitoring.Cron;

// Saves what ApiRequestLogMiddleware queued, in batches: one INSERT round-trip
// per batch instead of one per request, and none of it on the request path.
//
// A batch that fails to save is logged and dropped — retrying it forever would
// back the queue up behind a database outage, and these rows are monitoring,
// not records anyone is owed.
public class ApiRequestLogWriterBackgroundService : BackgroundService
{
    private const int MaxBatch = 500;
    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(2);

    private readonly IApiRequestLogQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ApiRequestLogWriterBackgroundService> _logger;

    public ApiRequestLogWriterBackgroundService(
        IApiRequestLogQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<ApiRequestLogWriterBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<ApiRequestLog>(MaxBatch);
        try
        {
            while (await _queue.Reader.WaitToReadAsync(stoppingToken))
            {
                // Give a burst a moment to fill the batch, then save whatever
                // arrived — a quiet hour still gets its few rows written.
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

        // What was queued in the last moments before shutdown.
        while (_queue.Reader.TryRead(out var row))
        {
            batch.Add(row);
            if (batch.Count >= MaxBatch) await SaveAsync(batch);
        }
        await SaveAsync(batch);
    }

    private async Task SaveAsync(List<ApiRequestLog> batch)
    {
        if (batch.Count == 0) return;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var monitoring = scope.ServiceProvider.GetRequiredService<IApiMonitoringService>();
            await monitoring.RecordBatchAsync(batch.ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save {Count} API request log rows; dropping them.", batch.Count);
        }
        finally
        {
            batch.Clear();
        }
    }
}
