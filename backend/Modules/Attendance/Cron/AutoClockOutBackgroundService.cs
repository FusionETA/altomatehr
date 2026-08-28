namespace AltomateHR.Api.Modules.Attendance.Cron;

// Sweeps open AttendanceSessions every SweepIntervalMinutes and closes any
// still open past CutoffMinutes — catches "employee forgot to clock out".
// Runs in-process (no external cron/secret needed); requires the app to
// stay running, which matches its existing long-lived Docker deployment.
//
// Idempotent: a session closed by one pass no longer matches
// GetOpenStartedBeforeAsync, so an overlapping run is harmless. Runs with no
// request context, so the tenant query filter is a no-op — one pass sweeps
// every org (matches DbSeeder's startup behavior, not a new pattern).
public class AutoClockOutBackgroundService : BackgroundService
{
    private const int SweepIntervalMinutes = 15;    // matches the reference app's cadence
    private const int MaxCandidatesPerSweep = 200;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AutoClockOutBackgroundService> _logger;

    public AutoClockOutBackgroundService(IServiceScopeFactory scopeFactory, ILogger<AutoClockOutBackgroundService> logger)
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
                var result = await attendance.RunAutoClockOutSweepAsync(MaxCandidatesPerSweep);
                if (result.ClockedOut > 0 || result.Errors > 0)
                {
                    _logger.LogInformation(
                        "Auto clock-out sweep: inspected {Inspected}, clocked out {ClockedOut}, errors {Errors}.",
                        result.Inspected, result.ClockedOut, result.Errors);
                }
            }
            catch (Exception ex)
            {
                // One bad sweep can't kill the timer loop — log and retry next interval.
                _logger.LogError(ex, "Auto clock-out sweep failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
