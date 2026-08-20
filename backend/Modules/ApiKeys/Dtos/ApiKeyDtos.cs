using System.ComponentModel.DataAnnotations;

namespace AltomateHR.Api.Modules.ApiKeys.Dtos;

// Owner creates a key: a label + the scopes it may use.
public class CreateApiKeyDto
{
    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    // Subset of ApiScopes.All. Unknown scopes are rejected by the service.
    public List<string> Scopes { get; set; } = new();
}

// Returned in the LIST — never includes the token, only its prefix.
public class ApiKeyDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string TokenPrefix { get; set; } = string.Empty;
    public IReadOnlyList<string> Scopes { get; set; } = Array.Empty<string>();
    public bool Active { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
}

// Returned ONCE, on creation — the only time the raw token is ever visible.
public class CreatedApiKeyDto : ApiKeyDto
{
    // The full "wp_live_..." token. Shown once; store it now or lose it.
    public string Token { get; set; } = string.Empty;
}
