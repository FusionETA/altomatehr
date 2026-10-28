using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.ApiKeys.Entities;

// A long-lived MACHINE credential ("wp_live_...") that lets an EXTERNAL app call the
// same endpoints as the frontend, authenticated by this key instead of a user login.
// Tied to exactly ONE org (the tenant): the key resolves to its org on every request,
// so machine traffic is scoped just like a signed-in admin of that org.
//
// SECURITY: only the SHA-256 HASH of the token is stored. The raw "wp_live_..." string
// is shown to the Owner exactly once, at creation, and can never be recovered.
public class ApiKey : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    // The tenant this key acts within. Left blank on create → StampTenant fills it
    // with the creating Owner's active org.
    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;

    // Human label so the Owner can tell keys apart in the list ("ABPay importer").
    [MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    // SHA-256(rawToken), hex. Unique — the lookup key on every request.
    [MaxLength(64)]
    public string TokenHash { get; set; } = string.Empty;

    // Plaintext first chars ("wp_live_abcd1234") kept for UX so the Owner can identify
    // a key in the list. NEVER the full token.
    [MaxLength(20)]
    public string TokenPrefix { get; set; } = string.Empty;

    // Comma-separated scope subset (e.g. "employees:read,claims:read") — what this key
    // may do. See ApiScopes for the catalog.
    [MaxLength(500)]
    public string Scopes { get; set; } = string.Empty;

    // Revoke = set false. An inactive key is rejected at authentication.
    public bool Active { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }

    // Set on a FAH key: a short-lived key minted for ONE signed-in admin so the
    // in-app assistant (through the MCP server) acts as that person — their
    // role, limits and name in the logs — instead of as an anonymous company
    // key. Null on an ordinary integration key.
    [MaxLength(40)]
    public string? ActingUserId { get; set; }

    // A FAH key stops working at this moment. Null = no expiry (integration keys).
    public DateTime? ExpiresAt { get; set; }
}
