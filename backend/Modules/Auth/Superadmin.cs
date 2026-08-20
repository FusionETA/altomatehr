using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace AltomateHR.Api.Modules.Auth;

// Superadmin = Fusioneta-side internal staff, identified by an EMAIL WHITELIST
// (SUPERADMIN_EMAILS, comma-separated) — NOT a per-org role. Ports the monolith's
// lib/auth/superadmin.ts. Used to gate PLATFORM actions like provisioning an org's plan
// (an Owner is a customer and must never set their own package).
//
// Detection is config/env-based so rotating support staff is a config change, and it is
// checked LIVE per request (never baked into the JWT), so dropping an email from the
// whitelist revokes access on the next request.
public interface ISuperadminRegistry
{
    bool IsSuperadmin(string? email);
}

public class SuperadminRegistry : ISuperadminRegistry
{
    private readonly HashSet<string> _emails;

    public SuperadminRegistry(IConfiguration config)
    {
        var raw = config["SUPERADMIN_EMAILS"] ?? string.Empty;
        _emails = raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.ToLowerInvariant())
            .ToHashSet();
    }

    public bool IsSuperadmin(string? email) =>
        !string.IsNullOrWhiteSpace(email) && _emails.Contains(email.Trim().ToLowerInvariant());
}

public static class AuthPolicies
{
    public const string Superadmin = "Superadmin";
}

// Requirement + handler behind [Authorize(Policy = AuthPolicies.Superadmin)].
public sealed class SuperadminRequirement : IAuthorizationRequirement { }

public sealed class SuperadminAuthorizationHandler : AuthorizationHandler<SuperadminRequirement>
{
    private readonly ISuperadminRegistry _registry;

    public SuperadminAuthorizationHandler(ISuperadminRegistry registry) => _registry = registry;

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, SuperadminRequirement requirement)
    {
        // Checked live against the whitelist. A wp_live key carries no email claim, so a
        // machine key can never be a superadmin.
        var email = context.User.FindFirst(ClaimTypes.Email)?.Value
                    ?? context.User.FindFirst(JwtRegisteredClaimNames.Email)?.Value
                    ?? context.User.FindFirst("email")?.Value;

        if (_registry.IsSuperadmin(email))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
