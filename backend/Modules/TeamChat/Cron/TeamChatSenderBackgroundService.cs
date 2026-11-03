namespace AltomateHR.Api.Modules.TeamChat.Cron;

// Posts what TeamChatAlerts queued, one message at a time, off the request
// path. A message that fails is logged and dropped: these are heads-ups for
// the team, not records anyone is owed, and retrying would back the queue up
// behind a Bitrix outage.
public class TeamChatSenderBackgroundService : BackgroundService
{
    // Bitrix webhooks allow about two calls a second; a burst of sign-ins at
    // 9am is spread out rather than refused.
    private static readonly TimeSpan Gap = TimeSpan.FromMilliseconds(600);

    private readonly ITeamChatQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TeamChatSenderBackgroundService> _logger;

    public TeamChatSenderBackgroundService(
        ITeamChatQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<TeamChatSenderBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var message in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(10));
                    await scope.ServiceProvider.GetRequiredService<IBitrixClient>().PostAsync(message, timeout.Token);
                }
                catch (Exception e) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogWarning(e, "Couldn't post an alert to the team chat.");
                }
                await Task.Delay(Gap, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down; whatever is still queued is dropped.
        }
    }
}
