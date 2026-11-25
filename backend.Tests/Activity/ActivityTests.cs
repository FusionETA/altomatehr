using System.Security.Claims;
using System.Threading.Channels;
using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Activity;
using AltomateHR.Api.Modules.Activity.Dtos;
using AltomateHR.Api.Modules.Activity.Entities;
using AltomateHR.Api.Modules.ApiMonitoring;
using AltomateHR.Api.Modules.ApiMonitoring.Entities;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Organizations.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Auth.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AltomateHR.Api.Tests.Activity;

// User activity is Fusioneta-only usage tracking. These pin what it may and may
// not record, and that a tracking failure never reaches the person signing in.
public class ActivityServiceTests
{
    private readonly InMemoryActivityRepository _repo = new();
    private readonly ActivityCurrentUser _user = new();

    private ActivityService Service() => new(
        _repo,
        new NoApiErrors(),
        new NoOrganizations(),
        _user,
        Options.Create(new ActivityOptions()),
        NullLogger<ActivityService>.Instance);

    [Fact]
    public async Task StartSession_RecordsWhoWhereAndHow()
    {
        _user.IpAddress = "182.62.46.105";
        _user.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/131.0 Safari/537.36";

        var id = await Service().StartSessionAsync(
            new SessionStart("usr-1", "a@globe.com", "Ahmad", "org-1", "GLOBE", "Employee", "password"));

        var session = Assert.Single(_repo.Sessions);
        Assert.Equal(id, session.Id);
        Assert.Equal("Ahmad", session.Name);
        Assert.Equal("GLOBE", session.CompanyName);
        Assert.Equal("password", session.Via);
        Assert.Equal("182.62.46.105", session.IpAddress);
        Assert.Equal("Chrome · Windows", session.Device);
        Assert.False(session.IsStaff);
    }

    [Fact]
    public async Task StartSession_NeverThrows_ItReturnsNull()
    {
        _repo.FailWrites = true;

        var id = await Service().StartSessionAsync(
            new SessionStart("usr-1", "a@globe.com", null, "org-1", null, "Employee", "password"));

        Assert.Null(id);
    }

    [Fact]
    public async Task Ingest_TakesIdentityFromTheToken_AndKeepsOnlyPagesAndClicks()
    {
        _user.SessionId = "sess-1";

        await Service().IngestAsync(new ActivityBatchDto
        {
            Events =
            [
                new() { Kind = "page", Name = "payroll/runs" },
                new() { Kind = "click", Name = "payslips.export" },
                new() { Kind = "action", Name = "POST claims" },            // only the server records actions
                new() { Kind = "page", Name = "John's salary is 5000" },   // free text is refused
                new() { Kind = "click", Name = "" },
            ],
        });

        Assert.Collection(_repo.Events,
            e => { Assert.Equal(ActivityKind.Page, e.Kind); Assert.Equal("payroll/runs", e.Name); },
            e => { Assert.Equal(ActivityKind.Click, e.Kind); Assert.Equal("payslips.export", e.Name); });
        Assert.All(_repo.Events, e =>
        {
            Assert.Equal("sess-1", e.SessionId);
            Assert.Equal("usr-1", e.UserId);
            Assert.Equal("org-1", e.OrganizationId);
        });
        var totals = Assert.Single(_repo.Totals);
        Assert.Equal((1, 1, 0), (totals.PageViews, totals.Clicks, totals.Actions));
    }

    [Fact]
    public async Task Ingest_WithoutASessionClaim_RecordsNothing()
    {
        _user.SessionId = null;

        await Service().IngestAsync(new ActivityBatchDto { Events = [new() { Kind = "page", Name = "dashboard" }] });

        Assert.Empty(_repo.Events);
    }

    [Fact]
    public async Task Ingest_AnEmptyBatch_IsAHeartbeat()
    {
        _user.SessionId = "sess-1";

        await Service().IngestAsync(new ActivityBatchDto());

        Assert.Equal(["sess-1"], _repo.Touched);
        Assert.Empty(_repo.Events);
    }

    [Fact]
    public void ClientTime_IsClampedToTheRecentPast()
    {
        var now = new DateTime(2026, 11, 25, 8, 0, 0, DateTimeKind.Utc);

        Assert.Equal(now, ActivityService.ClampClientTime(now.AddHours(2), now));
        Assert.Equal(now.AddMinutes(-15), ActivityService.ClampClientTime(now.AddDays(-3), now));
        Assert.Equal(now.AddMinutes(-1), ActivityService.ClampClientTime(now.AddMinutes(-1), now));
        Assert.Equal(now, ActivityService.ClampClientTime(null, now));
    }

    [Fact]
    public void Timeline_GivesEachPageItsTime_AndAFailedActionItsMessage()
    {
        var start = new DateTime(2026, 11, 25, 8, 0, 0, DateTimeKind.Utc);
        var session = new UserSession
        {
            Id = "sess-1", UserId = "usr-1", OrganizationId = "org-1",
            StartedAt = start, LastSeenAt = start.AddMinutes(10),
        };
        UserActivityEvent Ev(int minute, ActivityKind kind, string name, int? status = null) => new()
        {
            SessionId = "sess-1", UserId = "usr-1", OrganizationId = "org-1",
            Kind = kind, Name = name, StatusCode = status, CreatedAt = start.AddMinutes(minute),
        };
        var events = new[]
        {
            Ev(0, ActivityKind.Page, "dashboard"),
            Ev(2, ActivityKind.Page, "claims/mine"),
            Ev(3, ActivityKind.Action, "POST claims/{id}/approve", status: 409),
        };
        var errors = new[]
        {
            new ApiRequestLog { Method = "POST", Route = "claims/{id}/approve", StatusCode = 409,
                ErrorMessage = "Already approved.", CreatedAt = start.AddMinutes(3).AddMilliseconds(40) },
            new ApiRequestLog { Method = "GET", Route = "payslips/{id}/pdf", StatusCode = 500,
                ErrorMessage = "boom", CreatedAt = start.AddMinutes(5) },
        };

        var items = ActivityService.BuildTimeline(session, events, errors, new Dictionary<string, string>());

        Assert.Equal(4, items.Count);
        Assert.Equal(120_000, items[0].DurationMs);                 // dashboard → claims
        Assert.Equal(480_000, items[1].DurationMs);                 // claims → last seen
        Assert.Equal("Already approved.", items[2].Message);        // matched, not repeated
        Assert.Equal("error", items[3].Kind);                       // a failed read slots in
        Assert.Equal("GET payslips/{id}/pdf", items[3].Name);
    }

    [Fact]
    public void Status_IsReadFromLastSeenAndSignOut()
    {
        var now = DateTime.UtcNow;
        Assert.Equal("online", ActivityService.ToDto(new UserSession { LastSeenAt = now.AddMinutes(-1) }, now).Status);
        Assert.Equal("inactive", ActivityService.ToDto(new UserSession { LastSeenAt = now.AddMinutes(-20) }, now).Status);
        Assert.Equal("signed-out",
            ActivityService.ToDto(new UserSession { LastSeenAt = now, EndedAt = now }, now).Status);
    }

    [Theory]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 Version/17.0 Mobile/15E148 Safari/604.1", "Safari · iOS")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/131.0 Safari/537.36 Edg/131.0", "Edge · Windows")]
    [InlineData("Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 Chrome/131.0 Mobile Safari/537.36", "Chrome · Android")]
    [InlineData("curl/8.4.0", "Other")]
    public void DeviceName_IsCoarse(string userAgent, string expected) =>
        Assert.Equal(expected, DeviceName.From(userAgent));
}

// Against a real EF context: the activity repository shares the request's
// AppDbContext with the refresh-token save that follows a sign-in.
public class ActivityRepositoryTests
{
    [Fact]
    public async Task AFailedSessionInsert_DoesNotBreakTheRefreshTokenSaveAfterIt()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new FailUserSessionInserts())
            .Options;
        await using var db = new AppDbContext(options, new ActivityCurrentUser());
        var service = new ActivityService(
            new ActivityRepository(db), new NoApiErrors(), new NoOrganizations(), new ActivityCurrentUser(),
            Options.Create(new ActivityOptions()), NullLogger<ActivityService>.Instance);

        var id = await service.StartSessionAsync(
            new SessionStart("usr-1", "a@globe.com", null, "org-1", null, "Employee", "password"));
        await new RefreshTokenRepository(db).AddAsync(new RefreshToken
        {
            Token = "rt", UserId = "usr-1", Email = "a@globe.com", Role = "Employee", OrganizationId = "org-1",
            CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(7),
        });

        Assert.Null(id);
        Assert.Equal(1, await db.RefreshTokens.CountAsync());
    }

    private sealed class FailUserSessionInserts : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<UserSession>().Any(e => e.State == EntityState.Added))
                throw new InvalidOperationException("simulated failure inserting a session");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}

public class UserActivityMiddlewareTests
{
    [Fact]
    public void RecordsAPersonsWrite_WithTheRouteTemplateAndTarget()
    {
        var context = Request("POST", "claims/{id}/approve", "clm-42", sessionId: "sess-1");
        context.Response.StatusCode = 200;

        var row = UserActivityMiddleware.Build(context, DateTime.UtcNow, TimeSpan.FromMilliseconds(84), escaped: false);

        Assert.NotNull(row);
        Assert.Equal(ActivityKind.Action, row!.Kind);
        Assert.Equal("POST claims/{id}/approve", row.Name);
        Assert.Equal("clm-42", row.TargetId);
        Assert.Equal("sess-1", row.SessionId);
        Assert.Equal(200, row.StatusCode);
        Assert.Equal(84, row.DurationMs);
    }

    // API keys and partner apps never carry an activity session.
    [Fact]
    public void IgnoresACallerWithNoSession()
    {
        var context = Request("POST", "claims", null, sessionId: null);

        Assert.Null(UserActivityMiddleware.Build(context, DateTime.UtcNow, TimeSpan.Zero, escaped: false));
    }

    [Fact]
    public void AnEscapedExceptionIsRecordedAsA500()
    {
        var context = Request("DELETE", "claims/{id}", "clm-1", sessionId: "sess-1");

        var row = UserActivityMiddleware.Build(context, DateTime.UtcNow, TimeSpan.Zero, escaped: true);

        Assert.Equal(500, row!.StatusCode);
    }

    [Fact]
    public async Task ReadsAndTheTrackersOwnBatchesAreNotRecorded()
    {
        var queue = new CapturingQueue();
        var middleware = new UserActivityMiddleware(_ => Task.CompletedTask, queue, NullLogger<UserActivityMiddleware>.Instance);

        var get = Request("GET", "claims/{id}", "clm-1", sessionId: "sess-1");
        await middleware.InvokeAsync(get);
        var batch = Request("POST", "activity/events", null, sessionId: "sess-1");
        batch.Request.Path = "/activity/events";
        await middleware.InvokeAsync(batch);

        Assert.Empty(queue.Rows);
    }

    private static DefaultHttpContext Request(string method, string template, string? id, string? sessionId)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = "/" + template.Replace("{id}", id ?? "x");
        if (id is not null) context.Request.RouteValues["id"] = id;
        context.SetEndpoint(new RouteEndpoint(
            _ => Task.CompletedTask, RoutePatternFactory.Parse(template), 0, EndpointMetadataCollection.Empty, template));

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "usr-1"), new("org", "org-1") };
        if (sessionId is not null) claims.Add(new Claim("asid", sessionId));
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        return context;
    }

    private sealed class CapturingQueue : IActivityEventQueue
    {
        private readonly Channel<UserActivityEvent> _channel = Channel.CreateUnbounded<UserActivityEvent>();
        public List<UserActivityEvent> Rows { get; } = [];

        public bool TryEnqueue(UserActivityEvent row)
        {
            Rows.Add(row);
            return true;
        }

        public ChannelReader<UserActivityEvent> Reader => _channel.Reader;
    }
}

// ---- Test doubles (also used by AuthServiceTests) ----

internal sealed class RecordingActivityService : IActivityService
{
    public List<SessionStart> Started { get; } = [];
    public List<string> Touched { get; } = [];
    public List<string> Ended { get; } = [];
    public string? LastId { get; private set; }

    public Task<string?> StartSessionAsync(SessionStart start)
    {
        Started.Add(start);
        LastId = $"sess-new-{Started.Count}";
        return Task.FromResult<string?>(LastId);
    }

    public Task EndSessionAsync(string? sessionId)
    {
        if (sessionId is not null) Ended.Add(sessionId);
        return Task.CompletedTask;
    }

    public Task TouchSessionAsync(string? sessionId)
    {
        if (sessionId is not null) Touched.Add(sessionId);
        return Task.CompletedTask;
    }

    public Task IngestAsync(ActivityBatchDto batch) => Task.CompletedTask;
    public Task RecordBatchAsync(IReadOnlyList<UserActivityEvent> events) => Task.CompletedTask;
    public Task<int> PurgeExpiredAsync() => Task.FromResult(0);
    public Task<SessionPageDto> ListSessionsAsync(SessionQueryDto query) => throw new NotSupportedException();
    public Task<SessionDetailDto?> GetSessionAsync(string id) => throw new NotSupportedException();
}

internal sealed class InMemoryActivityRepository : IActivityRepository
{
    public List<UserSession> Sessions { get; } = [];
    public List<UserActivityEvent> Events { get; } = [];
    public List<SessionTotals> Totals { get; } = [];
    public List<string> Touched { get; } = [];
    public bool FailWrites { get; set; }

    public Task AddSessionAsync(UserSession session)
    {
        if (FailWrites) throw new InvalidOperationException("the database is on fire");
        Sessions.Add(session);
        return Task.CompletedTask;
    }

    public Task<UserSession?> GetSessionAsync(string id) => Task.FromResult(Sessions.FirstOrDefault(s => s.Id == id));
    public Task EndSessionAsync(string id, DateTime at, string reason) => Task.CompletedTask;

    public Task TouchSessionAsync(string id, DateTime at)
    {
        Touched.Add(id);
        return Task.CompletedTask;
    }

    public Task AddEventsAsync(IReadOnlyList<UserActivityEvent> events)
    {
        Events.AddRange(events);
        return Task.CompletedTask;
    }

    public Task ApplySessionTotalsAsync(IReadOnlyList<SessionTotals> totals)
    {
        Totals.AddRange(totals);
        return Task.CompletedTask;
    }

    public Task<(List<UserSession> Rows, int Total)> ListSessionsAsync(SessionFilter filter) =>
        Task.FromResult((Sessions.ToList(), Sessions.Count));

    public Task<List<UserActivityEvent>> GetEventsAsync(string sessionId, int limit) =>
        Task.FromResult(Events.Where(e => e.SessionId == sessionId).Take(limit).ToList());

    public Task<int> DeleteEventsOlderThanAsync(DateTime cutoff) => Task.FromResult(0);
    public Task<int> DeleteSessionsLastSeenBeforeAsync(DateTime cutoff) => Task.FromResult(0);
}

internal sealed class ActivityCurrentUser : ICurrentUser
{
    public string? UserId { get; set; } = "usr-1";
    public string? OrganizationId { get; set; } = "org-1";
    public string? Email { get; set; } = "a@globe.com";
    public string? Role { get; set; } = "Employee";
    public bool IsAdmin => false;
    public bool IsAuthenticated => UserId is not null;
    public string? IpAddress { get; set; }
    public string? SessionId { get; set; }
    public string? UserAgent { get; set; }
}

internal sealed class NoApiErrors : IApiRequestLogRepository
{
    public Task<List<ApiRequestLog>> GetErrorsForCallerAsync(string callerId, DateTime from, DateTime to, int limit) =>
        Task.FromResult(new List<ApiRequestLog>());

    public Task AddRangeAsync(IReadOnlyList<ApiRequestLog> rows) => throw new NotSupportedException();
    public Task<int> DeleteOlderThanAsync(DateTime cutoff) => throw new NotSupportedException();
    public Task<List<ApiEndpointStats>> GetEndpointStatsAsync(DateTime from, DateTime to, string? organizationId) =>
        throw new NotSupportedException();
    public Task<List<ApiOrganizationCalls>> GetCallsPerOrganizationAsync(DateTime from, DateTime to) =>
        throw new NotSupportedException();
    public Task<List<ApiRequestLog>> GetErrorsAsync(
        DateTime from, DateTime to, string? organizationId, string? method, string? route, int? status, int limit) =>
        throw new NotSupportedException();
}

internal sealed class NoOrganizations : IOrganizationRepository
{
    public Task<Organization?> GetByIdAsync(string id) => Task.FromResult<Organization?>(null);
    public Task<Organization?> GetFirstAsync() => throw new NotSupportedException();
    public Task<List<Organization>> GetAllAsync() => throw new NotSupportedException();
    public Task AddAsync(Organization organization) => throw new NotSupportedException();
    public Task UpdateAsync(Organization organization) => throw new NotSupportedException();
    public Task<bool> AnyAsync() => throw new NotSupportedException();
}
