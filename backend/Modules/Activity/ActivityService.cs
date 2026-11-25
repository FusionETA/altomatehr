using System.Text.RegularExpressions;
using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Activity.Dtos;
using AltomateHR.Api.Modules.Activity.Entities;
using AltomateHR.Api.Modules.ApiMonitoring;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Organizations;
using Microsoft.Extensions.Options;

namespace AltomateHR.Api.Modules.Activity;

public partial class ActivityService : IActivityService
{
    // Seen within this long (the tracker heartbeats every minute while the tab
    // is visible) and not signed out = online.
    public static readonly TimeSpan OnlineWindow = TimeSpan.FromMinutes(3);

    // A browser may batch a little; anything claimed older than this is pulled
    // forward, so a wrong clock cannot write into the past.
    private static readonly TimeSpan MaxClientLag = TimeSpan.FromMinutes(15);

    // A page left open over lunch is not 90 minutes of work on it.
    private static readonly TimeSpan MaxPageDuration = TimeSpan.FromMinutes(30);

    public const int MaxTimelineEvents = 2000;
    private const int MaxTimelineErrors = 200;
    private const int MaxPageSize = 100;

    // A page or data-track id: vocabulary, never free text.
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._/:\-]{0,159}$")]
    private static partial Regex NamePattern();

    private readonly IActivityRepository _repo;
    private readonly IApiRequestLogRepository _apiLogs;
    private readonly IOrganizationRepository _organizations;
    private readonly ICurrentUser _currentUser;
    private readonly ISuperadminRegistry? _superadmins;
    private readonly ActivityOptions _options;
    private readonly ILogger<ActivityService> _logger;

    public ActivityService(
        IActivityRepository repo,
        IApiRequestLogRepository apiLogs,
        IOrganizationRepository organizations,
        ICurrentUser currentUser,
        IOptions<ActivityOptions> options,
        ILogger<ActivityService> logger,
        ISuperadminRegistry? superadmins = null)
    {
        _repo = repo;
        _apiLogs = apiLogs;
        _organizations = organizations;
        _currentUser = currentUser;
        _options = options.Value;
        _logger = logger;
        _superadmins = superadmins;
    }

    // ---- Sessions ----

    public async Task<string?> StartSessionAsync(SessionStart start)
    {
        try
        {
            var now = DateTime.UtcNow;
            var session = new UserSession
            {
                UserId = start.UserId,
                Email = Truncate(start.Email, 200)!,
                Name = Truncate(string.IsNullOrWhiteSpace(start.Name) ? start.Email : start.Name.Trim(), 200)!,
                OrganizationId = start.OrganizationId,
                CompanyName = Truncate(start.CompanyName ?? string.Empty, 200)!,
                Role = Truncate(start.Role, 20)!,
                Via = Truncate(start.Via, 40)!,
                IpAddress = Truncate(_currentUser.IpAddress, 64),
                Device = Truncate(DeviceName.From(_currentUser.UserAgent), 100),
                IsStaff = _superadmins?.IsSuperadmin(start.Email) == true,
                StartedAt = now,
                LastSeenAt = now,
            };
            await _repo.AddSessionAsync(session);
            return session.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not start an activity session for user {UserId}.", start.UserId);
            return null;
        }
    }

    public async Task EndSessionAsync(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return;
        try
        {
            await _repo.EndSessionAsync(sessionId, DateTime.UtcNow, SessionEndReasons.SignedOut);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not end activity session {SessionId}.", sessionId);
        }
    }

    public async Task TouchSessionAsync(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return;
        try
        {
            await _repo.TouchSessionAsync(sessionId, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not touch activity session {SessionId}.", sessionId);
        }
    }

    // ---- Recording ----

    public async Task IngestAsync(ActivityBatchDto batch)
    {
        var sessionId = _currentUser.SessionId;
        var userId = _currentUser.UserId;
        // A token minted before tracking existed has no "sid" until its next
        // refresh — nothing to attach the events to yet.
        if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(userId)) return;

        var now = DateTime.UtcNow;
        var events = new List<UserActivityEvent>();
        foreach (var e in batch.Events)
        {
            var kind = e.Kind switch
            {
                "page" => ActivityKind.Page,
                "click" => ActivityKind.Click,
                _ => (ActivityKind?)null,
            };
            if (kind is null || !NamePattern().IsMatch(e.Name ?? string.Empty)) continue;

            events.Add(new UserActivityEvent
            {
                SessionId = sessionId,
                UserId = userId,
                OrganizationId = _currentUser.OrganizationId,
                Kind = kind.Value,
                Name = e.Name!,
                IsSupport = _currentUser.IsSupport,
                CreatedAt = ClampClientTime(e.At, now),
            });
        }

        if (events.Count > 0) await RecordBatchAsync(events);
        else await _repo.TouchSessionAsync(sessionId, now);   // a heartbeat
    }

    public async Task RecordBatchAsync(IReadOnlyList<UserActivityEvent> events)
    {
        if (events.Count == 0) return;
        await _repo.AddEventsAsync(events);
        await _repo.ApplySessionTotalsAsync(Totals(events));
    }

    // Per session in the batch: how many of each, and the latest time seen.
    public static List<SessionTotals> Totals(IEnumerable<UserActivityEvent> events) =>
        events
            .GroupBy(e => e.SessionId)
            .Select(g => new SessionTotals(
                g.Key,
                g.Max(e => e.CreatedAt),
                g.Count(e => e.Kind == ActivityKind.Page),
                g.Count(e => e.Kind == ActivityKind.Click),
                g.Count(e => e.Kind == ActivityKind.Action),
                g.Count(e => e.Kind == ActivityKind.Action && e.StatusCode >= 400)))
            .ToList();

    public static DateTime ClampClientTime(DateTime? at, DateTime now)
    {
        if (at is null) return now;
        var utc = at.Value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(at.Value, DateTimeKind.Utc)
            : at.Value.ToUniversalTime();
        if (utc > now) return now;
        var floor = now - MaxClientLag;
        return utc < floor ? floor : utc;
    }

    public async Task<int> PurgeExpiredAsync()
    {
        // Floored at one day: a misconfigured 0 must not empty the tables every night.
        var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, _options.RetentionDays));
        var events = await _repo.DeleteEventsOlderThanAsync(cutoff);
        var sessions = await _repo.DeleteSessionsLastSeenBeforeAsync(cutoff);
        return events + sessions;
    }

    // ---- Reading ----

    public async Task<SessionPageDto> ListSessionsAsync(SessionQueryDto query)
    {
        var now = DateTime.UtcNow;
        var to = query.To?.ToUniversalTime() ?? now.AddMinutes(1);
        var from = query.From?.ToUniversalTime() ?? to.AddDays(-7);
        var limit = Math.Clamp(query.Limit, 1, MaxPageSize);
        var page = Math.Max(1, query.Page);

        var (rows, total) = await _repo.ListSessionsAsync(new SessionFilter(
            from,
            to,
            Blank(query.OrganizationId),
            Blank(query.Search),
            Blank(query.Via),
            query.OnlineOnly ? now - OnlineWindow : null,
            query.IncludeStaff,
            (page - 1) * limit,
            limit));

        return new SessionPageDto { Sessions = rows.Select(s => ToDto(s, now)).ToList(), Total = total };
    }

    public async Task<SessionDetailDto?> GetSessionAsync(string id)
    {
        var session = await _repo.GetSessionAsync(id);
        if (session is null) return null;

        var now = DateTime.UtcNow;
        // The newest steps when there are too many: one extra is fetched only
        // to know that older ones were left out.
        var events = await _repo.GetEventsAsync(id, MaxTimelineEvents + 1);
        var truncated = events.Count > MaxTimelineEvents;
        if (truncated) events = events.Skip(1).ToList();

        // The API request log keys a person by user id, not session — so a
        // window around this session. (Two sessions of the same person open at
        // once would share their failed reads; rare, and harmless to show.)
        var end = (session.EndedAt ?? session.LastSeenAt).AddMinutes(1);
        var start = truncated ? events[0].CreatedAt : session.StartedAt;
        var errors = await _apiLogs.GetErrorsForCallerAsync(
            session.UserId, start.AddSeconds(-5), end, MaxTimelineErrors);

        var otherOrgIds = events
            .Select(e => e.OrganizationId)
            .Where(o => o is not null && o != session.OrganizationId)
            .Distinct()
            .ToList();
        var companyNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var orgId in otherOrgIds)
        {
            var org = await _organizations.GetByIdAsync(orgId!);
            if (org is not null) companyNames[org.Id] = org.Name;
        }

        return new SessionDetailDto
        {
            Session = ToDto(session, now),
            Items = BuildTimeline(session, events, errors, companyNames),
            Truncated = truncated,
        };
    }

    // Events in order, with: each page's time-on-page; each failed action's
    // error message matched from the API request log; and failed READS (which
    // are not activity events) slotted in as "error" items.
    public static List<TimelineItemDto> BuildTimeline(
        UserSession session,
        IReadOnlyList<UserActivityEvent> events,
        IReadOnlyList<ApiMonitoring.Entities.ApiRequestLog> errors,
        IReadOnlyDictionary<string, string> companyNames)
    {
        var lastSign = session.EndedAt ?? session.LastSeenAt;
        var unmatched = errors.ToList();
        var items = new List<TimelineItemDto>(events.Count + errors.Count);

        for (var i = 0; i < events.Count; i++)
        {
            var e = events[i];
            var item = new TimelineItemDto
            {
                At = e.CreatedAt,
                Kind = e.Kind.ToString().ToLowerInvariant(),
                Name = e.Name,
                TargetId = e.TargetId,
                StatusCode = e.StatusCode,
                DurationMs = e.DurationMs,
                IsSupport = e.IsSupport,
                CompanyName = e.OrganizationId is not null && e.OrganizationId != session.OrganizationId
                    ? companyNames.GetValueOrDefault(e.OrganizationId)
                    : null,
            };

            if (e.Kind == ActivityKind.Page)
            {
                var next = events.Skip(i + 1).FirstOrDefault(n => n.Kind == ActivityKind.Page)?.CreatedAt ?? lastSign;
                var span = next - e.CreatedAt;
                item.DurationMs = span <= TimeSpan.Zero
                    ? null
                    : (int)Math.Min(span.TotalMilliseconds, MaxPageDuration.TotalMilliseconds);
            }
            else if (e.Kind == ActivityKind.Action && e.StatusCode >= 400)
            {
                var spaceAt = e.Name.IndexOf(' ');
                var method = spaceAt > 0 ? e.Name[..spaceAt] : e.Name;
                var route = spaceAt > 0 ? e.Name[(spaceAt + 1)..] : string.Empty;
                var match = unmatched.FirstOrDefault(r =>
                    r.Method == method && r.Route == route && r.StatusCode == e.StatusCode
                    && Math.Abs((r.CreatedAt - e.CreatedAt).TotalSeconds) <= 5);
                if (match is not null)
                {
                    item.Message = match.ErrorMessage;
                    unmatched.Remove(match);
                }
            }

            items.Add(item);
        }

        // Failed writes not matched above belong to requests that were never
        // activity (another tab's session, an auth call) — only reads are added.
        items.AddRange(unmatched
            .Where(r => string.Equals(r.Method, "GET", StringComparison.OrdinalIgnoreCase))
            .Select(r => new TimelineItemDto
            {
                At = r.CreatedAt,
                Kind = "error",
                Name = $"{r.Method} {r.Route}",
                StatusCode = r.StatusCode,
                DurationMs = r.DurationMs,
                Message = r.ErrorMessage,
            }));

        return items.OrderBy(i => i.At).ToList();
    }

    public static SessionDto ToDto(UserSession s, DateTime now) => new()
    {
        Id = s.Id,
        UserId = s.UserId,
        Name = s.Name,
        Email = s.Email,
        OrganizationId = s.OrganizationId,
        CompanyName = s.CompanyName,
        Role = s.Role,
        Via = s.Via,
        IpAddress = s.IpAddress,
        Device = s.Device,
        IsStaff = s.IsStaff,
        StartedAt = s.StartedAt,
        LastSeenAt = s.LastSeenAt,
        EndedAt = s.EndedAt,
        EndReason = s.EndReason,
        Status = s.EndedAt is not null ? "signed-out"
            : s.LastSeenAt >= now - OnlineWindow ? "online"
            : "inactive",
        PageViews = s.PageViews,
        Clicks = s.Clicks,
        Actions = s.Actions,
        FailedActions = s.FailedActions,
    };

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
