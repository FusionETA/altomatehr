using AltomateHR.Api.Modules.Attendance.Entities;
using AltomateHR.Api.Modules.Teams;

namespace AltomateHR.Api.Modules.Attendance.Cron;

// Periodically logs, per reviewer, how many attendance/break approvals are
// waiting on them — the "digest reminder" job, scoped down to detection-only
// (no Notifications module to actually deliver a push/email). Same detection
// logic is exposed on-demand per-caller at GET /attendance/pending-approvals/digest.
// No Redis-backed dedup here (the reference app's push-throttling logic) —
// every tick just logs the current snapshot.
public class ApprovalDigestBackgroundService : BackgroundService
{
    private const int SweepIntervalMinutes = 30;   // matches the reference app's cadence
    private const ApprovalModule Module = ApprovalModule.ATTENDANCE;

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
                var approvalRequests = scope.ServiceProvider.GetRequiredService<IAttendanceApprovalRequestRepository>();
                var router = scope.ServiceProvider.GetRequiredService<IApprovalRouter>();

                var pending = await approvalRequests.GetOpenByKindsAsync(
                    Enum.GetValues<AttendanceApprovalKind>());

                var countByReviewer = new Dictionary<string, int>();
                foreach (var request in pending)
                {
                    var approvers = await router.CurrentApproversAsync(Module, request.EmployeeId, request.CurrentStep);
                    foreach (var reviewerId in approvers)
                        countByReviewer[reviewerId] = countByReviewer.GetValueOrDefault(reviewerId) + 1;
                }

                if (countByReviewer.Count > 0)
                {
                    _logger.LogInformation(
                        "Pending approvals waiting on {ReviewerCount} reviewer(s): {Summary}",
                        countByReviewer.Count,
                        string.Join(", ", countByReviewer.Select(kv => $"{kv.Key}={kv.Value}")));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Approval-digest sweep failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
