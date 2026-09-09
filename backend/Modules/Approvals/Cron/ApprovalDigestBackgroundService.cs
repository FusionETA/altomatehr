namespace AltomateHR.Api.Modules.Approvals.Cron;

// Fires the cross-module pending-approval digest once a day at 06:00
// Asia/Kuala_Lumpur — the same timezone AttendanceTime.DefaultTimeZone and
// the Leave accrual/rollover sweeps already use for their own day-boundary
// checks, so this doesn't introduce a new convention.
//
// Ticks every 30 minutes (the cadence this job used back when it only
// logged Attendance's own counts) and is gated on BOTH the hour and a
// last-run-date marker, so it fires exactly once per calendar day even
// though multiple ticks land inside the 06:00-06:29 window. Same idiom as
// LeaveAccrualBackgroundService/LeaveRolloverBackgroundService (day check +
// last-run marker, set only on success so a failed run retries within the
// same window rather than waiting a full day).
//
// Formerly Modules/Attendance/Cron/ApprovalDigestBackgroundService — moved
// here once it stopped calling IAttendanceService directly: it's now purely
// "run the cross-module digest on a schedule," which belongs next to
// ApprovalDigestService/ApprovalReconciliationService, not under Attendance.
public class ApprovalDigestBackgroundService : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(30);
    private const int FireHourMyt = 6;

    private static readonly TimeZoneInfo Myt = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kuala_Lumpur");

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ApprovalDigestBackgroundService> _logger;

    private DateOnly? _lastRunDate;

    public ApprovalDigestBackgroundService(
        IServiceScopeFactory scopeFactory, ILogger<ApprovalDigestBackgroundService> logger)
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
                var digest = scope.ServiceProvider.GetRequiredService<IApprovalDigestService>();
                var result = await digest.RunAsync();

                _lastRunDate = today;
                _logger.LogInformation(
                    "Approval digest: notified {ReviewerCount} reviewer(s).", result.ReviewersNotified);
            }
            catch (Exception ex)
            {
                // Leave _lastRunDate unset so the next tick (still within the
                // 06:00-06:29 window) retries today.
                _logger.LogError(ex, "Approval-digest run failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
