namespace AltomateHR.Api.Modules.Attendance.Cron;

// Periodically logs, per reviewer, how many attendance/break approvals are
// waiting on them — the "digest reminder" job, scoped down to detection-only
// (no Notifications module to actually deliver a push/email). Same detection
// logic is exposed on-demand per-caller at GET /attendance/pending-approvals/digest.
// No Redis-backed dedup here (the reference app's push-throttling logic) —
// every tick just logs the current snapshot.
//
// The per-reviewer aggregation lives in AttendanceService.GetOrgApprovalDigestAsync
// (a scoped service, resolved fresh each tick) — this cron only schedules it and
// logs the result, keeping repository access behind the service layer.
public class ApprovalDigestBackgroundService : BackgroundService
{
    private const int SweepIntervalMinutes = 30;   // matches the reference app's cadence

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ApprovalDigestBackgroundService> _logger;

    public ApprovalDigestBackgroundService(IServiceScopeFactory scopeFactory, ILogger<ApprovalDigestBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(SweepIntervalMinutes));
        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var attendance = scope.ServiceProvider.GetRequiredService<IAttendanceService>();
                var digest = await attendance.GetOrgApprovalDigestAsync();

                if (digest.Count > 0)
                {
                    _logger.LogInformation(
                        "Pending approvals waiting on {ReviewerCount} reviewer(s): {Summary}",
                        digest.Count,
                        string.Join(", ", digest.Select(e => $"{e.ReviewerId}={e.PendingCount}")));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Approval-digest sweep failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
