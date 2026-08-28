namespace AltomateHR.Api.Modules.Partners.Dtos;

// POST /partner/token — body. The client secret rides in the Authorization
// header (Bearer), not here.
public class PartnerTokenRequestDto
{
    public string Ticket { get; set; } = string.Empty;
}

// POST /partner/token/refresh — body.
public class PartnerRefreshRequestDto
{
    public string RefreshToken { get; set; } = string.Empty;
}

// The token-exchange result. `user` + `organization` let the partner find/create
// its own local record (storing only the IDs) without a second round-trip.
public class PartnerTokenResponseDto
{
    public string AccessToken { get; set; } = string.Empty;
    public string? RefreshToken { get; set; }
    public int ExpiresIn { get; set; }                 // seconds
    public PartnerUserDto User { get; set; } = new();
    public PartnerOrgDto Organization { get; set; } = new();
}

public class PartnerUserDto
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;   // the user's role IN this org
}

public class PartnerOrgDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
