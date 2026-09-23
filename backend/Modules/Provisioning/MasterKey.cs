using System.ComponentModel.DataAnnotations;

namespace AltomateHR.Api.Modules.Provisioning;

// A credential that belongs to NO organization.
//
// Every other credential here is org-scoped — a wp_live_ key resolves to its
// one tenant on every request, which is what makes the tenant filter safe. This
// one exists because provisioning has a bootstrap problem: creating a company
// cannot require a credential that already belongs to one.
//
// Deliberately NOT an ApiKey row. ApiKey is ITenantScoped, so it is auto-
// stamped and auto-filtered by org; a row with no org would be invisible to its
// own lookup. Keeping this a separate table also means the one credential that
// escapes tenant isolation is a table you can read in full, rather than a flag
// hidden among hundreds of ordinary keys.
//
// SECURITY: only the SHA-256 hash is stored, like ApiKey. The raw
// "wp_master_..." string exists once, at creation, and is never recoverable.
// It is issued out of band by support, never by a customer action.
public class MasterKey
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    // Who holds it — "Altomate Accounting (prod)". The only way to tell two
    // apart in a list, since the token itself is unrecoverable.
    [MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(64)]
    public string TokenHash { get; set; } = string.Empty;

    // First characters, for identifying a row without revealing the token.
    [MaxLength(24)]
    public string TokenPrefix { get; set; } = string.Empty;

    // Revocation without deletion, so the audit trail keeps pointing at a real
    // row after a key is withdrawn.
    public bool Active { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    // Answers "is this still in use?" before revoking one.
    public DateTime? LastUsedAt { get; set; }
}
