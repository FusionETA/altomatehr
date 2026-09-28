namespace AltomateHR.Api.Modules.Auth;

public interface ITokenService
{
    string CreateToken(string userId, string email, string role, string organizationId);  // access token (JWT)

    // The same, marked as a Fusioneta support session (claim "support" = "1")
    // when `support` is true — see AuthService.EnterSupportAsync.
    string CreateToken(string userId, string email, string role, string organizationId, bool support) =>
        CreateToken(userId, email, role, organizationId);
    string CreateRefreshToken();                                                           // opaque random string
}
