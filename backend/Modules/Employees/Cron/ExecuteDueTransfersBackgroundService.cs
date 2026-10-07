namespace AltomateHR.Api.Modules.Employees.Cron;

// Runs every employee transfer whose effective date has arrived (the
// monolith's /api/cron/execute-pending-transfers). A transfer dated today runs
// inline when it's scheduled, so this only picks up future-dated ones — and
// retries FAILED ones until they run or someone cancels them.
//
// Follows the house pattern (ArchivePastLeaversBackgroundService): in-process,
// no external cron. Hourly rather than daily so a transfer dated tomorrow lands
// shortly after local midnight, not up to a day late.
//
// Each transfer gets its OWN scope: a move that fails half-built leaves
// unsaved changes in its DbContext, and recording the failure through that
// same context would save them. The failure is written from a fresh scope.
public class ExecuteDueTransfersBackgroundService : BackgroundService
{
    private const int SweepIntervalHours = 1;
    private const int MaxPerSweep = 200;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExecuteDueTransfersBackgroundService> _logger;

    public ExecuteDueTransfersBackgroundService(
        IServiceScopeFactory scopeFactory, ILogger<ExecuteDueTransfersBackgroundService> logger)
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
                await SweepAsync();
            }
            catch (Exception ex)
            {
                // One bad sweep must not kill the timer loop.
                _logger.LogError(ex, "Employee transfer sweep failed; retrying next interval.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SweepAsync()
    {
        List<string> due;
        using (var scope = _scopeFactory.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IEmployeeTransferRepository>();
            var today = Attendance.AttendanceTime.StartOfLocalDay(DateTime.UtcNow);
            due = await repo.GetDueIdsAsync(today, MaxPerSweep);
        }

        foreach (var id in due)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IEmployeeTransferService>().ExecuteDueAsync(id);
                _logger.LogInformation("Executed employee transfer {TransferId}.", id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Employee transfer {TransferId} failed; will retry.", id);
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var message = ex is TransferException ? ex.Message : "Unexpected error — see server logs.";
                    await scope.ServiceProvider.GetRequiredService<IEmployeeTransferService>()
                        .MarkFailedAsync(id, message);
                }
                catch (Exception markEx)
                {
                    _logger.LogError(markEx, "Could not record failure on transfer {TransferId}.", id);
                }
            }
        }
    }
}
