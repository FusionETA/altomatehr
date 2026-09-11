using AltomateHR.Api.Modules.Employees;

namespace AltomateHR.Api.Modules.Payroll.Cron;

// Daily sweep: archive every employee profile whose leave date has passed
// but which is still marked active.
//
// This has **no financial effect** — `PayrollRunService` already excludes a
// past-leave-date employee from generation, so a leaver is not paid either
// way. It is bookkeeping: without it the Active tab of the employee list
// fills with departed staff and the "last working day" banner sits on stale
// profiles forever.
//
// The case it exists for is the planned leaver. An admin sets a FUTURE leave
// date months ahead and saves — correctly leaving the person active — and
// then nobody opens that profile again. Nothing else ever fires.
//
// Follows the house pattern (AutoClockOutBackgroundService): in-process, no
// external cron or shared secret. Runs with no request context, so the
// tenant query filter is a no-op and one pass covers every org.
//
// Idempotent: an archived profile no longer matches the query, so a repeated
// or overlapping pass is a no-op.
public class ArchivePastLeaversBackgroundService : BackgroundService
{
    private const int SweepIntervalHours = 12;
    private const int MaxPerSweep = 500;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ArchivePastLeaversBackgroundService> _logger;

    public ArchivePastLeaversBackgroundService(
        IServiceScopeFactory scopeFactory, ILogger<ArchivePastLeaversBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(SweepIntervalHours));

        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var sweeper = scope.ServiceProvider.GetRequiredService<IPastLeaverArchiver>();

                var archived = await sweeper.SweepAsync(MaxPerSweep);
                if (archived > 0)
                {
                    _logger.LogInformation(
                        "Archived {Count} employee profile(s) whose leave date has passed.", archived);
                }
            }
            catch (Exception ex)
            {
                // One bad sweep must not kill the timer loop.
                _logger.LogError(ex, "Past-leaver archive sweep failed; retrying next interval.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

public interface IPastLeaverArchiver
{
    // Returns how many profiles were archived. Capped per pass so one very
    // large org cannot monopolise a sweep.
    Task<int> SweepAsync(int max);
}

public class PastLeaverArchiver : IPastLeaverArchiver
{
    private readonly IEmployeeProfileRepository _profiles;

    public PastLeaverArchiver(IEmployeeProfileRepository profiles) => _profiles = profiles;

    public async Task<int> SweepAsync(int max)
    {
        // Anchored to UTC midnight, matching how leave dates are stored. A
        // leave date of TODAY does not match — someone's last working day is
        // a day they are still employed, and tomorrow's sweep picks them up.
        var today = DateTime.UtcNow.Date;

        var leavers = await _profiles.GetUnarchivedPastLeaversAsync(today, max);

        foreach (var profile in leavers)
        {
            profile.IsArchived = true;
            profile.ArchivedAt = DateTime.UtcNow;
            profile.ArchiveReason = "Leave date passed";
            await _profiles.UpdateAsync(profile);
        }

        return leavers.Count;
    }
}
