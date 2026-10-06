namespace AltomateHR.Api.Modules.Auth;

public interface ITokenService
{
    string CreateToken(string userId, string email, string role, string organizationId);  // access token (JWT)

    // The same, marked as a Fusioneta support session (claim "support" = "1")
    // when `support` is true — see AuthService.EnterSupportAsync — and as a
    // session that arrived through the Altomate SSO hand-off (claim "sso" = "1")
    // when `sso` is true. `former` marks a session in a company the person no
    // longer works at (claim "former" = "1"), which is view-only.
    string CreateToken(
        string userId, string email, string role, string organizationId, bool support, bool sso = false, bool former = false) =>
        CreateToken(userId, email, role, organizationId);
    string CreateRefreshToken();                                                           // opaque random string
}
