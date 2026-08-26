namespace AltomateHR.Api.Modules.Attendance.Cron;

// Periodically checks for employees still clocked in past ThresholdMinutes
// and logs the count — the "reminder to clock out" job, scoped down to
// detection-only since there's no Notifications module to actually deliver
// a push/in-app alert. Same detection logic is exposed on-demand at
// GET /attendance/warnings/still-clocked-in for an admin to check directly.
public class OtWarningBackgroundService : BackgroundService
{
    private const int ThresholdMinutes = 600;       // 10h — "still clocked in" cutoff
    private const int SweepIntervalMinutes = 60;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OtWarningBackgroundService> _logger;

    public OtWarningBackgroundService(IServiceScopeFactory scopeFactory, ILogger<OtWarningBackgroundService> logger)
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
                var warnings = (await attendance.GetStillClockedInWarningsAsync(ThresholdMinutes)).ToList();
                if (warnings.Count > 0)
                {
                    _logger.LogWarning(
                        "{Count} employee(s) still clocked in past {Threshold} minutes: {EmployeeIds}",
                        warnings.Count, ThresholdMinutes, string.Join(", ", warnings.Select(w => w.EmployeeId)));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OT-warning sweep failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
