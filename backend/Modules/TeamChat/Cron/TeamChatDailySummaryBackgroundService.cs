namespace AltomateHR.Api.Modules.TeamChat.Cron;

// Posts the daily summary to the team's daily chat at 08:00
// Asia/Kuala_Lumpur. Same idiom as ApprovalDigestBackgroundService: a
// 30-minute tick gated on the hour and a last-run date, set only on success
// so a failed run retries within the same hour.
public class TeamChatDailySummaryBackgroundService : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(30);
    private const int FireHourMyt = 8;

    private static readonly TimeZoneInfo Myt = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kuala_Lumpur");

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TeamChatDailySummaryBackgroundService> _logger;

    private DateOnly? _lastRunDate;

    public TeamChatDailySummaryBackgroundService(
        IServiceScopeFactory scopeFactory, ILogger<TeamChatDailySummaryBackgroundService> logger)
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
                if (nowMyt.Hour != FireHourMyt) continue;

                var today = DateOnly.FromDateTime(nowMyt);
                if (_lastRunDate == today) continue;

                using var scope = _scopeFactory.CreateScope();
                var posted = await scope.ServiceProvider
                    .GetRequiredService<ITeamChatDailySummaryService>().PostAsync(today);

                _lastRunDate = today;
                _logger.LogInformation("Team chat daily summary: {Count} message(s) queued.", posted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Team chat daily summary failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
