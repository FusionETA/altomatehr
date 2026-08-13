namespace AltomateHR.Api.Modules.Auth;

// What AuthService hands back to the controller:
//  - AccessToken / Email / Role  → go in the response body
//  - RefreshToken / expiry       → the controller uses these to set the httpOnly cookie
// The service never touches cookies (that's an HTTP concern the controller owns).
public record AuthResult(
    string AccessToken,
    string Email,
    string Role,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt);
