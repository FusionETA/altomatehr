namespace AltomateHR.Api.Modules.Auth;

// What AuthService hands back to the controller:
//  - AccessToken / Email / Role / OrganizationId → go in the response body
//  - RefreshToken / expiry                       → the controller sets the httpOnly cookie
// Role + OrganizationId reflect the ACTIVE org the token was minted for.
public record AuthResult(
    string AccessToken,
    string Email,
    string Role,
    string OrganizationId,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt,
    string? Name = null,
    // Fusioneta staff (SUPERADMIN_EMAILS) — shows the Support page.
    bool IsSuperadmin = false,
    // This session is support mode inside OrganizationId.
    bool SupportMode = false,
    string? OrganizationName = null,
    // Arrived through the Altomate SSO hand-off.
    bool ViaSso = false,
    // The active company is one this person no longer works at — view-only.
    bool FormerEmployee = false);

// An org the signed-in account can act in (drives the org switcher). Role is the
// account's role IN THAT org — Employee here, Supervisor there, etc. Name is the
// company name, so the switcher can list them by name rather than by opaque id.
//
// IsFormer: they no longer work there (archived — left, or transferred out).
// Still listed so they can read old payslips; the switcher marks it "Former".
// Outcome of "Leave company". Error non-null → 400. Otherwise SignOut means no
// company is left; Next (when set) is the session to switch to.
public record LeaveOrgResult(string? Error, AuthResult? Next = null, bool SignOut = false);

public record UserOrgDto(string OrganizationId, string Name, string Role, bool IsFormer = false);
