namespace AltomateHR.Api.Modules.Auth.Dtos;

// What login/refresh/switch-org returns to the client: the JWT + a little user
// info. Role + ActiveOrganizationId describe the org the token is scoped to.
public class AuthResponseDto
{
    public string Token { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    // The signed-in person's real name; null when their profile has none.
    public string? Name { get; set; }
    public string Role { get; set; } = string.Empty;
    public string ActiveOrganizationId { get; set; } = string.Empty;
}
