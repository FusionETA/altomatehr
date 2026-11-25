using AltomateHR.Api.Modules.Activity.Dtos;
using AltomateHR.Api.Modules.Activity.Entities;

namespace AltomateHR.Api.Modules.Activity;

// Who signed in, and what they did afterwards — for Fusioneta only.
//
// The session methods are called from sign-in, refresh and sign-out, so like
// IAuditService they NEVER THROW: tracking that breaks must not stop anyone
// signing in. A failed StartSessionAsync returns null and the session simply
// goes untracked.
public interface IActivityService
{
    // A sign-in. Returns the new session's id, which goes into the token as "sid".
    Task<string?> StartSessionAsync(SessionStart start);

    // An explicit sign-out. A null id (an untracked session) is a no-op.
    Task EndSessionAsync(string? sessionId);

    // Still here (a refresh). A null id is a no-op.
    Task TouchSessionAsync(string? sessionId);

    // The browser tracker's batch, for the current signed-in user. Events are
    // dropped silently when invalid — a tracker bug must not surface to anyone.
    Task IngestAsync(ActivityBatchDto batch);

    // Saves events and rolls their counts into their sessions. Background writer only.
    Task RecordBatchAsync(IReadOnlyList<UserActivityEvent> events);

    // Deletes everything past the retention window. Rows deleted.
    Task<int> PurgeExpiredAsync();

    // ---- Fusioneta Support tab (superadmin-gated by the controller) ----

    Task<SessionPageDto> ListSessionsAsync(SessionQueryDto query);

    // Null when there is no such session.
    Task<SessionDetailDto?> GetSessionAsync(string id);
}

public sealed record SessionStart(
    string UserId,
    string Email,
    string? Name,
    string OrganizationId,
    string? CompanyName,
    string Role,
    string Via);
