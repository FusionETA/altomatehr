using AltomateHR.Api.Modules.ApiKeys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AltomateHR.Api.Modules.Provisioning;

// The only surface a MASTER key opens.
//
// [AllowAnonymous] on purpose: the master token is not a JWT and not a
// wp_live_ key, so none of the registered auth schemes recognise it. It is
// verified here, explicitly, against its stored hash — and nothing else in the
// app accepts it. Keeping that check in one visible place is better than a
// scheme that silently applies everywhere.
[ApiController]
[Route("admin")]
[AllowAnonymous]
public class ProvisioningController : ControllerBase
{
    private readonly IProvisioningService _provisioning;

    public ProvisioningController(IProvisioningService provisioning) =>
        _provisioning = provisioning;

    // POST /admin/organizations — create a company and its integration key.
    //
    // Rate limited: this is an unauthenticated route until the token is checked,
    // so the check itself is the thing worth limiting. Without one it is an
    // offline-speed guessing machine against the one credential that escapes
    // tenant isolation.
    [EnableRateLimiting("auth-login")]
    [HttpPost("organizations")]
    public async Task<IActionResult> CreateOrganization(CreateOrganizationRequest request)
    {
        if (await _provisioning.AuthenticateAsync(BearerToken()) is null)
            return Unauthorized(new { message = "Invalid or revoked master key." });

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { message = "Organization name is required." });

        var scopes = (request.Scopes is { Count: > 0 } asked ? asked : ApiScopes.All)
            .Distinct()
            .ToList();

        var unknown = scopes.Where(s => !ApiScopes.IsKnown(s)).ToList();
        if (unknown.Count > 0)
            return BadRequest(new { message = $"Unknown scope(s): {string.Join(", ", unknown)}." });

        var created = await _provisioning.CreateOrganizationAsync(request.Name, scopes);

        // The raw key appears here and nowhere else, ever. A caller that loses
        // it cannot recover it — only be issued a new one.
        return Ok(new
        {
            organizationId = created.OrganizationId,
            name = created.Name,
            apiKey = created.ApiKey,
            apiKeyPrefix = created.ApiKeyPrefix,
            scopes,
        });
    }

    private string? BearerToken()
    {
        var header = Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : null;
    }
}

public class CreateOrganizationRequest
{
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.MaxLength(200)]
    public string? Name { get; set; }

    // Omitted means every scope — a provisioning caller that just created the
    // company is the one party entitled to full access to it. Narrowing is
    // available for a caller that wants least privilege.
    public List<string>? Scopes { get; set; }
}
