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
    DateTime RefreshTokenExpiresAt);

// An org the signed-in account can act in (drives the org switcher). Role is the
// account's role IN THAT org — Employee here, Supervisor there, etc.
public record UserOrgDto(string OrganizationId, string Role);
