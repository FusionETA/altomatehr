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
    public string? ActiveOrganizationName { get; set; }

    // Fusioneta staff: the Support page is available.
    public bool IsSuperadmin { get; set; }

    // Support mode — acting inside a customer's org as Fusioneta support.
    public bool SupportMode { get; set; }

    // Signed in through Altomate (SSO): the account is managed there, so the
    // app hides New company, Change password and Log out — as the previous
    // system did.
    public bool ViaSso { get; set; }

    // The active company is one they no longer work at (archived there after
    // leaving or a transfer). The portal shows payslips only; writes are refused.
    public bool FormerEmployee { get; set; }
}
