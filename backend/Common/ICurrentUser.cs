namespace AltomateHR.Api.Common;

// Reads the current request's identity from the validated JWT (one place, injectable).
// Services get "who + which org" without touching HttpContext themselves.
public interface ICurrentUser
{
    string? UserId { get; }
    string? OrganizationId { get; }

    // The signed-in address. Already minted into the JWT — surfaced here so the
    // audit log can name a person rather than a GUID, without a directory
    // lookup on every write.
    string? Email { get; }
    string? Role { get; }
    bool IsAdmin { get; }
    bool IsAuthenticated { get; }

    // The caller's remote IP, for the attendance IP-allowlist check. Null when
    // there's no request context (background jobs, seeding) — callers treat
    // null as "can't verify" rather than "allowed".
    string? IpAddress { get; }

    // A Fusioneta superadmin in support mode: signed in as themselves but
    // acting inside a customer's org they are not a member of. Their actions
    // are audited as "System (Support)" there, with the real person recorded
    // in the internal support log.
    bool IsSupport => false;

    // Arrived through the Altomate SSO hand-off (claim "sso") rather than a
    // password sign-in here. The account is managed in Altomate.
    bool IsSso => false;

    // Signed into a company they no longer work at (claim "former"): archived
    // there after leaving or a transfer. View-only — see
    // FormerEmployeeReadOnlyMiddleware.
    bool IsFormer => false;

    // A machine caller narrowed by scopes: a wp_live_ key or a partner app.
    // People (JWT) are never scoped.
    bool IsScopedMachine => false;

    // May the caller use this API scope (ApiScopes, e.g. "employees:read")?
    // Always true for a person — scopes only narrow machine access — and for a
    // scoped machine, only when its key holds it. The service-side twin of
    // [RequireScope], for a service that reads or writes another module's data
    // and must ask about that module's scope too.
    bool HasScope(string scope) => true;
}
