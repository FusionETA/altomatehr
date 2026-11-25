using System.ComponentModel.DataAnnotations;

namespace AltomateHR.Api.Modules.Activity.Entities;

// One row per sign-in — the same events the "🔑 Sign-in" alerts post to the
// Fusioneta team chat. Everything the person does afterwards (UserActivityEvent)
// hangs off this row's Id, which rides in the access token as the "sid" claim
// and on the refresh token, so a refresh, a page reload or a company switch
// stays inside the same session.
//
// NOT tenant-scoped, like ApiRequestLog: it is an internal log read across every
// company by Fusioneta superadmins only, never by a customer. OrganizationId is
// set explicitly at sign-in. The ONLY way to read it is PlatformActivityController,
// which is superadmin-gated — never expose it from a customer-facing endpoint.
public class UserSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    // ---- Who (copied, like the audit log: still says who it was after a rename) ----

    [MaxLength(40)]
    public string UserId { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Email { get; set; } = string.Empty;

    // The company they signed in to. A company switch later in the session is
    // recorded on each event, not here.
    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;

    [MaxLength(200)]
    public string CompanyName { get; set; } = string.Empty;

    [MaxLength(20)]
    public string Role { get; set; } = string.Empty;

    // ---- How ----

    // "password", "Altomate SSO", or "resumed" (a session already open before
    // activity tracking existed, picked up at its next refresh).
    [MaxLength(40)]
    public string Via { get; set; } = string.Empty;

    [MaxLength(64)]
    public string? IpAddress { get; set; }

    // "Chrome · Windows" — a short reading of the user agent, not the raw string.
    [MaxLength(100)]
    public string? Device { get; set; }

    // On SUPERADMIN_EMAILS at sign-in: Fusioneta's own staff, hidden by default.
    public bool IsStaff { get; set; }

    // ---- When ----

    public DateTime StartedAt { get; set; }

    // Bumped by every event, heartbeat and refresh. "Online" is read off this.
    public DateTime LastSeenAt { get; set; }

    // Only on an explicit sign-out. A session that just stops (tab closed,
    // token expired) keeps EndedAt null and reads as inactive from LastSeenAt.
    public DateTime? EndedAt { get; set; }

    [MaxLength(20)]
    public string? EndReason { get; set; }

    // ---- Running totals, so the sign-ins list needs no join ----

    public int PageViews { get; set; }
    public int Clicks { get; set; }
    public int Actions { get; set; }
    public int FailedActions { get; set; }
}

public static class SessionEndReasons
{
    public const string SignedOut = "signed-out";
}

public static class SessionVia
{
    public const string Password = "password";
    public const string AltomateSso = "Altomate SSO";
    public const string Resumed = "resumed";
}
