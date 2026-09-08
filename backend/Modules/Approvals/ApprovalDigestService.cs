using AltomateHR.Api.Modules.Approvals.Dtos;
using AltomateHR.Api.Modules.Attendance;
using AltomateHR.Api.Modules.Claims;
using AltomateHR.Api.Modules.Leave;
using AltomateHR.Api.Modules.Notifications;
using AltomateHR.Api.Modules.Notifications.Entities;
using AltomateHR.Api.Modules.Overtime;

namespace AltomateHR.Api.Modules.Approvals;

// Asks each module for its own pending-approval count per reviewer and sends
// one combined notification per reviewer — same "ask each module for one
// number and combine them" shape as ApprovalReconciliationService, for a
// different job.
public class ApprovalDigestService : IApprovalDigestService
{
    // Every module key that ends up in a reviewer's breakdown, always present
    // even at zero — see the class doc on ApprovalDigestEntryDto.
    private static readonly string[] ModuleKeys = ["CLAIMS", "LEAVE", "ATTENDANCE", "OT"];

    private readonly IAttendanceService _attendance;
    private readonly ILeaveService _leave;
    private readonly IClaimsService _claims;
    private readonly IOvertimeService _overtime;
    private readonly INotificationService _notifications;

    public ApprovalDigestService(
        IAttendanceService attendance,
        ILeaveService leave,
        IClaimsService claims,
        IOvertimeService overtime,
        INotificationService notifications)
    {
        _attendance = attendance;
        _leave = leave;
        _claims = claims;
        _overtime = overtime;
        _notifications = notifications;
    }

    public async Task<ApprovalDigestRunResultDto> RunAsync()
    {
        var byModule = new Dictionary<string, IReadOnlyList<(string ReviewerId, string OrgId, int Count)>>
        {
            ["CLAIMS"] = (await _claims.GetOrgApprovalDigestAsync())
                .Select(e => (e.ReviewerId, e.OrganizationId, e.PendingCount)).ToList(),
            ["LEAVE"] = (await _leave.GetOrgApprovalDigestAsync())
                .Select(e => (e.ReviewerId, e.OrganizationId, e.PendingCount)).ToList(),
            ["ATTENDANCE"] = (await _attendance.GetOrgApprovalDigestAsync())
                .Select(e => (e.ReviewerId, e.OrganizationId, e.PendingCount)).ToList(),
            ["OT"] = (await _overtime.GetOrgApprovalDigestAsync())
                .Select(e => (e.ReviewerId, e.OrganizationId, e.PendingCount)).ToList(),
        };

        var breakdownByReviewer = new Dictionary<(string ReviewerId, string OrgId), Dictionary<string, int>>();
        foreach (var (module, entries) in byModule)
        {
            foreach (var entry in entries)
            {
                var key = (entry.ReviewerId, entry.OrgId);
                if (!breakdownByReviewer.TryGetValue(key, out var breakdown))
                {
                    // Seeded with every module at zero so a reviewer with, say,
                    // only claims pending still gets told "0 leave, 0 attendance,
                    // 0 overtime" rather than those modules just not appearing.
                    breakdown = ModuleKeys.ToDictionary(m => m, _ => 0);
                    breakdownByReviewer[key] = breakdown;
                }
                breakdown[module] = entry.Count;
            }
        }

        var sent = new List<ApprovalDigestEntryDto>();
        foreach (var ((reviewerId, orgId), breakdown) in breakdownByReviewer)
        {
            var total = breakdown.Values.Sum();
            if (total == 0) continue;   // nothing pending anywhere — no notification

            await _notifications.NotifyAsync(
                orgId, reviewerId, NotificationType.APPROVAL_DIGEST,
                $"You have {total} pending approval{(total == 1 ? "" : "s")}",
                BuildBody(breakdown),
                url: null);   // no single "all approvals" page exists to deep-link to

            sent.Add(new ApprovalDigestEntryDto(reviewerId, orgId, total, breakdown));
        }

        return new ApprovalDigestRunResultDto(sent.Count, sent);
    }

    // Always names all four modules by count, in the same order every time —
    // the point of this digest is a reviewer can read it alone and know
    // exactly where to go first, without opening the app.
    private static string BuildBody(IReadOnlyDictionary<string, int> byModule) =>
        $"{CountLabel(byModule["CLAIMS"], "claim")}, " +
        $"{CountLabel(byModule["LEAVE"], "leave request")}, " +
        $"{CountLabel(byModule["ATTENDANCE"], "attendance request")}, and " +
        $"{CountLabel(byModule["OT"], "overtime request")} awaiting your review.";

    private static string CountLabel(int count, string singular) =>
        $"{count} {singular}{(count == 1 ? "" : "s")}";
}
