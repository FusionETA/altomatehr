using System.ComponentModel.DataAnnotations;

namespace AltomateHR.Api.Modules.Partners.Entities;

// A registered external application (e.g. Appraisify) allowed to exchange a
// single-use launch ticket for a short-lived, org-scoped, read-only access token.
//
// NOT tenant-scoped — one row serves EVERY customer org. That's the whole point
// of the client-secret model (see the integration spec): Appraisify holds one
// secret for all customers, and the per-org data key is minted fresh each session
// and stored in Redis, never here. This row is durable *config*, not ephemeral.
public class ApiClient
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    // Human name AND the /sso/launch/{app} slug (e.g. "appraisify").
    [MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    // SHA-256 of the client secret we issue the app. The raw secret is delivered
    // out-of-band (password manager) and NEVER stored — only this hash.
    [MaxLength(64)]
    public string SecretHash { get; set; } = string.Empty;

    // csv of granted resource scopes drawn from ApiScopes, e.g. "employees:read".
    // The grant is per-app; the vocabulary is shared across apps.
    [MaxLength(500)]
    public string Scopes { get; set; } = string.Empty;

    // Where /sso/launch/{app} redirects the browser, with ?t=<ticket> appended.
    [MaxLength(300)]
    public string RedirectUrl { get; set; } = string.Empty;

    // Baked into every access token this app receives, so a leaked token can't be
    // replayed against a different consumer.
    [MaxLength(80)]
    public string Audience { get; set; } = string.Empty;

    // Kill switch — flip to false to cut the app off without touching employee data.
    public bool Active { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
