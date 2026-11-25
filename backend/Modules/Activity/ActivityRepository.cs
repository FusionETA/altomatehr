using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Activity.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Activity;

// The only place that touches UserSessions / UserActivityEvents. Neither table
// is tenant-scoped (see UserSession), so nothing here is filtered by the
// current org — which is why every read is behind the superadmin gate.
public class ActivityRepository : IActivityRepository
{
    private readonly AppDbContext _db;

    public ActivityRepository(AppDbContext db) => _db = db;

    public async Task AddSessionAsync(UserSession session)
    {
        _db.UserSessions.Add(session);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch
        {
            // This context is the request's, shared with the refresh-token save
            // that follows a sign-in. Left tracked, the failed row would be
            // retried by THAT save and take the sign-in down with it — the
            // caller swallows this so tracking can never block a sign-in.
            _db.Entry(session).State = EntityState.Detached;
            throw;
        }
    }

    public Task<UserSession?> GetSessionAsync(string id) =>
        _db.UserSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);

    public Task EndSessionAsync(string id, DateTime at, string reason) =>
        _db.UserSessions
            .Where(s => s.Id == id && s.EndedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.EndedAt, at)
                .SetProperty(x => x.EndReason, reason)
                .SetProperty(x => x.LastSeenAt, x => x.LastSeenAt > at ? x.LastSeenAt : at));

    public Task TouchSessionAsync(string id, DateTime at) =>
        _db.UserSessions
            .Where(s => s.Id == id && s.LastSeenAt < at)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSeenAt, at));

    public async Task AddEventsAsync(IReadOnlyList<UserActivityEvent> events)
    {
        if (events.Count == 0) return;
        _db.UserActivityEvents.AddRange(events);
        await _db.SaveChangesAsync();
    }

    public async Task ApplySessionTotalsAsync(IReadOnlyList<SessionTotals> totals)
    {
        foreach (var t in totals)
        {
            await _db.UserSessions
                .Where(s => s.Id == t.SessionId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.LastSeenAt, x => x.LastSeenAt > t.LastSeenAt ? x.LastSeenAt : t.LastSeenAt)
                    .SetProperty(x => x.PageViews, x => x.PageViews + t.PageViews)
                    .SetProperty(x => x.Clicks, x => x.Clicks + t.Clicks)
                    .SetProperty(x => x.Actions, x => x.Actions + t.Actions)
                    .SetProperty(x => x.FailedActions, x => x.FailedActions + t.FailedActions));
        }
    }

    public async Task<(List<UserSession> Rows, int Total)> ListSessionsAsync(SessionFilter filter)
    {
        // A session lasts as long as its refresh-token chain — someone who
        // never logs out keeps one for weeks. So "in this range" means ACTIVE
        // in it (overlaps it), not started in it; and online-now ignores the
        // range altogether.
        var query = _db.UserSessions.AsNoTracking()
            .Where(s => s.StartedAt < filter.To);
        if (filter.OnlineSince is null) query = query.Where(s => s.LastSeenAt >= filter.From);

        if (filter.OrganizationId is not null) query = query.Where(s => s.OrganizationId == filter.OrganizationId);
        if (filter.Via is not null) query = query.Where(s => s.Via == filter.Via);
        if (!filter.IncludeStaff) query = query.Where(s => !s.IsStaff);
        if (filter.OnlineSince is { } since)
            query = query.Where(s => s.EndedAt == null && s.LastSeenAt >= since);
        if (filter.Search is not null)
            query = query.Where(s => s.Name.Contains(filter.Search) || s.Email.Contains(filter.Search));

        var total = await query.CountAsync();
        var rows = await query
            // Most recently active first: a long-running session that is in
            // use right now belongs at the top, not under weeks of sign-ins.
            .OrderByDescending(s => s.LastSeenAt)
            .Skip(filter.Skip)
            .Take(filter.Take)
            .ToListAsync();
        return (rows, total);
    }

    // The NEWEST `limit`, returned oldest first: in a weeks-long session, today
    // is what someone is looking for.
    public async Task<List<UserActivityEvent>> GetEventsAsync(string sessionId, int limit)
    {
        var newest = await _db.UserActivityEvents.AsNoTracking()
            .Where(e => e.SessionId == sessionId)
            .OrderByDescending(e => e.CreatedAt)
            .Take(limit)
            .ToListAsync();
        newest.Reverse();
        return newest;
    }

    // One statement each, not load-and-remove: a day can be tens of thousands of rows.
    public Task<int> DeleteEventsOlderThanAsync(DateTime cutoff) =>
        _db.UserActivityEvents.Where(e => e.CreatedAt < cutoff).ExecuteDeleteAsync();

    public Task<int> DeleteSessionsLastSeenBeforeAsync(DateTime cutoff) =>
        _db.UserSessions.Where(s => s.LastSeenAt < cutoff).ExecuteDeleteAsync();
}
