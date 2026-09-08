using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Attendance.Dtos;
using AltomateHR.Api.Modules.Attendance.Entities;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Teams;

namespace AltomateHR.Api.Modules.Attendance;

public class AdminAttendanceService : IAdminAttendanceService
{
    // Read against the SLA when the org has not set one. Matches the entity
    // default so the report and the settings screen never disagree.
    private const int DefaultSlaMinutes = 60;

    private readonly IAttendanceApprovalRequestRepository _approvals;
    private readonly IAttendanceRepository _records;
    private readonly IAttendancePhotoStorage _photos;
    private readonly IEmployeeRowResolver _employees;
    private readonly ITeamService _teams;
    private readonly IOrganizationService _organizations;
    private readonly ICurrentUser _currentUser;

    public AdminAttendanceService(
        IAttendanceApprovalRequestRepository approvals,
        IAttendanceRepository records,
        IAttendancePhotoStorage photos,
        IEmployeeRowResolver employees,
        ITeamService teams,
        IOrganizationService organizations,
        ICurrentUser currentUser)
    {
        _approvals = approvals;
        _records = records;
        _photos = photos;
        _employees = employees;
        _teams = teams;
        _organizations = organizations;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<SupervisorPerformanceDto>> GetSupervisorPerformanceAsync(
        DateTime from, DateTime to, string? projectId, string? teamId, string? q)
    {
        var scope = await ScopeAsync(projectId, teamId, q);
        if (scope is { Count: 0 }) return [];

        var slaMinutes = await SlaMinutesAsync();
        var directory = await _employees.GetSnapshotAsync();

        // Decided rows only: a pending request has no reviewer to attribute it
        // to, and counting it against whoever eventually picks it up would make
        // a fast supervisor look slow for inheriting a queue.
        var decided = (await _approvals.GetForAuditAsync(null, from.Date, to.Date.AddDays(1), 5000))
            .Where(a => a.ApprovalStatus is AttendanceApprovalStatus.APPROVED
                                         or AttendanceApprovalStatus.REJECTED)
            .Where(a => !string.IsNullOrEmpty(a.ReviewerId) && a.DecidedAt is not null)
            .Where(a => scope is null || scope.Contains(a.EmployeeId))
            .ToList();

        return decided
            .GroupBy(a => a.ReviewerId!)
            .Select(g =>
            {
                var delays = g
                    .Select(a => (a.DecidedAt!.Value - a.SubmittedAt).TotalMinutes)
                    // A clock skew or a backdated import can produce a negative
                    // delay. Floored rather than dropped: the decision happened,
                    // and silently excluding it would undercount the reviewer.
                    .Select(m => Math.Max(0, m))
                    .ToList();

                return new SupervisorPerformanceDto
                {
                    ReviewerId = g.Key,
                    ReviewerName = Label(directory, g.Key),
                    TotalDecisions = g.Count(),
                    ApprovedCount = g.Count(a => a.ApprovalStatus == AttendanceApprovalStatus.APPROVED),
                    RejectedCount = g.Count(a => a.ApprovalStatus == AttendanceApprovalStatus.REJECTED),
                    SlowDecisionCount = delays.Count(m => m > slaMinutes),
                    AvgDelayMinutes = delays.Count == 0 ? null : Math.Round(delays.Average(), 1),
                    // The worst case sits beside the average because an average
                    // of 40 minutes hides the one request that waited three days.
                    MaxDelayMinutes = delays.Count == 0 ? null : Math.Round(delays.Max(), 1),
                };
            })
            .OrderByDescending(r => r.SlowDecisionCount)
            .ThenByDescending(r => r.AvgDelayMinutes ?? 0)
            .ToList();
    }

    public async Task<IReadOnlyList<ApprovalAuditEntryDto>> GetApprovalAuditAsync(
        DateTime from, DateTime to, string? projectId, string? teamId, string? q)
    {
        var scope = await ScopeAsync(projectId, teamId, q);
        if (scope is { Count: 0 }) return [];

        var directory = await _employees.GetSnapshotAsync();
        var now = DateTime.UtcNow;

        return (await _approvals.GetForAuditAsync(null, from.Date, to.Date.AddDays(1), 2000))
            .Where(a => scope is null || scope.Contains(a.EmployeeId))
            .Select(a => new ApprovalAuditEntryDto
            {
                Id = a.Id,
                EmployeeId = a.EmployeeId,
                EmployeeName = Label(directory, a.EmployeeId),
                Kind = a.Kind.ToString(),
                Status = a.ApprovalStatus.ToString(),
                EventAt = a.EventAt,
                SubmittedAt = a.SubmittedAt,
                DecidedAt = a.DecidedAt,
                ReviewerId = a.ReviewerId,
                ReviewerName = a.ReviewerId is null ? null : Label(directory, a.ReviewerId),
                ReviewNotes = a.ReviewNotes,
                // Still-pending rows measure to NOW, so "waiting four days" reads
                // on the same scale as a decision that took four days.
                DelayMinutes = Math.Round(
                    Math.Max(0, ((a.DecidedAt ?? now) - a.SubmittedAt).TotalMinutes), 1),
            })
            .OrderByDescending(a => a.SubmittedAt)
            .ToList();
    }

    public async Task<SelfieStorageDto> GetSelfieStorageAsync()
    {
        var records = await _records.GetWithPhotosAsync();

        var fileNames = records
            .SelectMany(r => new[] { r.ClockInPhotoUrl, r.ClockOutPhotoUrl })
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => FileNameOf(url!))
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        long bytes = 0;
        var found = 0;
        var missing = 0;

        foreach (var name in fileNames)
        {
            var file = await _photos.GetAsync(name);

            // Sized by stat, not by reading. GetAsync hands back a path, and
            // loading every selfie into memory to measure it would make a
            // storage report the heaviest request in the app.
            var info = file is null ? null : new FileInfo(file.Path);
            if (info is null || !info.Exists)
            {
                // Pointed at but not on disk. Not an error — a pruned or
                // manually removed file — but it is also exactly what a broken
                // upload path looks like, so it is counted rather than ignored.
                missing++;
                continue;
            }

            found++;
            bytes += info.Length;
        }

        return new SelfieStorageDto
        {
            PhotoCount = found,
            TotalBytes = bytes,
            MissingCount = missing,
            OldestPhotoAt = records.Count == 0 ? null : records.Min(r => r.Date),
        };
    }

    // The employee ids a filter narrows to, or null for "everyone".
    //
    // Null and empty mean different things and callers must not conflate them:
    // null is no filter at all, empty is a filter that matched nobody — which
    // has to return no rows rather than the whole org.
    private async Task<HashSet<string>?> ScopeAsync(string? projectId, string? teamId, string? q)
    {
        HashSet<string>? scope = null;

        if (!string.IsNullOrWhiteSpace(teamId))
        {
            scope = (await _teams.GetMemberEmployeeIdsAsync(teamId)).ToHashSet(StringComparer.Ordinal);
        }

        if (!string.IsNullOrWhiteSpace(projectId))
        {
            // Employees reach a project through their teams, so "on this
            // project" is the union of the rosters of every team on it.
            var onProject = (await _teams.GetAllAsync())
                .Where(t => t.ProjectId == projectId)
                .SelectMany(t => t.Members.Select(m => m.EmployeeId))
                .ToHashSet(StringComparer.Ordinal);

            scope = scope is null ? onProject : scope.Intersect(onProject).ToHashSet(StringComparer.Ordinal);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            var directory = await _employees.GetSnapshotAsync();
            var matched = directory.Members
                .Where(m =>
                    (m.Name ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    (m.Email ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase))
                .Select(m => m.Id)
                .ToHashSet(StringComparer.Ordinal);

            scope = scope is null ? matched : scope.Intersect(matched).ToHashSet(StringComparer.Ordinal);
        }

        return scope;
    }

    private async Task<int> SlaMinutesAsync()
    {
        var org = await _organizations.GetByIdAsync(_currentUser.OrganizationId ?? string.Empty);
        var minutes = org?.SupervisorSlaMinutes ?? 0;
        return minutes > 0 ? minutes : DefaultSlaMinutes;
    }

    private static string Label(EmployeeRowIndex directory, string userId)
    {
        var name = directory.NameOf(userId);
        if (!string.IsNullOrWhiteSpace(name)) return name;

        var email = directory.EmailOf(userId);
        return string.IsNullOrWhiteSpace(email) ? userId : email;
    }

    // Photo urls are stored as the path the client fetches; storage addresses
    // files by bare name.
    private static string FileNameOf(string url) =>
        url.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty;
}
