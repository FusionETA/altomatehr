namespace AltomateHR.Api.Modules.DocumentExpiry.Cron;

// Sends the day's work permit and passport reminders at 08:00 Malaysian time.
// Ticks every 30 minutes and runs once inside the 08:00 hour, like
// ApprovalDigestBackgroundService. Safe to run twice (a restart, a second
// instance): each reminder is recorded and never sent again.
public class DocumentExpiryReminderBackgroundService : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(30);
    public const int FireHourMyt = 8;

    private static readonly TimeZoneInfo Myt = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kuala_Lumpur");

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DocumentExpiryReminderBackgroundService> _logger;

    private DateOnly? _lastRunDate;

    public DocumentExpiryReminderBackgroundService(
        IServiceScopeFactory scopeFactory, ILogger<DocumentExpiryReminderBackgroundService> logger)
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
                var reminders = scope.ServiceProvider.GetRequiredService<IDocumentExpiryReminderService>();
                var result = await reminders.RunAsync(nowMyt.Date);

                _lastRunDate = today;
                _logger.LogInformation(
                    "Document expiry reminders: {Reminders} in {Companies} company(ies); {AdminEmails} admin and {StaffEmails} staff email(s).",
                    result.Reminders, result.Companies, result.AdminEmails, result.StaffEmails);
            }
            catch (Exception ex)
            {
                // Left unset so the next tick in the same hour retries.
                _logger.LogError(ex, "Document expiry reminder run failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
