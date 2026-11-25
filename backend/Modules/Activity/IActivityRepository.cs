using AltomateHR.Api.Modules.Activity.Entities;

namespace AltomateHR.Api.Modules.Activity;

public interface IActivityRepository
{
    Task AddSessionAsync(UserSession session);

    Task<UserSession?> GetSessionAsync(string id);

    // Marks the session signed out — once: a second sign-out keeps the first time.
    Task EndSessionAsync(string id, DateTime at, string reason);

    // Moves LastSeenAt forward (never back).
    Task TouchSessionAsync(string id, DateTime at);

    Task AddEventsAsync(IReadOnlyList<UserActivityEvent> events);

    // Adds a batch's counts to each session's running totals and moves its
    // LastSeenAt forward — one UPDATE per session in the batch.
    Task ApplySessionTotalsAsync(IReadOnlyList<SessionTotals> totals);

    Task<(List<UserSession> Rows, int Total)> ListSessionsAsync(SessionFilter filter);

    // The newest `limit` events, in order (oldest of them first).
    Task<List<UserActivityEvent>> GetEventsAsync(string sessionId, int limit);

    // Rows deleted.
    Task<int> DeleteEventsOlderThanAsync(DateTime cutoff);
    Task<int> DeleteSessionsLastSeenBeforeAsync(DateTime cutoff);
}

public sealed record SessionTotals(
    string SessionId, DateTime LastSeenAt, int PageViews, int Clicks, int Actions, int FailedActions);

public sealed record SessionFilter(
    DateTime From,
    DateTime To,
    string? OrganizationId,
    string? Search,
    string? Via,
    // Seen at or after this, and not signed out — regardless of From. Null = any.
    DateTime? OnlineSince,
    bool IncludeStaff,
    int Skip,
    int Take);
