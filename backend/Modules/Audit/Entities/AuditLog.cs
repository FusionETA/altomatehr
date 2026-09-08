using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Audit.Entities;

// Who did what, to what, and whether it worked — one row per event, per org.
//
// Append-only by contract: nothing in the app updates or deletes a row, and the
// hash chain below makes an out-of-band edit detectable. See AuditChain.
public class AuditLog : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;

    // ---- Hash chain (see AuditChain) ----
    //
    // Seq is per-organization and 1-based; Hash covers this row's own fields
    // PLUS PrevHash, so editing any row invalidates every row after it. Two orgs
    // write independent chains and never block each other.
    public int Seq { get; set; }

    [MaxLength(64)]
    public string Hash { get; set; } = string.Empty;        // hex SHA-256

    [MaxLength(64)]
    public string? PrevHash { get; set; }                   // null on the first row

    // ---- Actor ----
    //
    // Email and name are COPIED rather than joined: an audit row has to still
    // say who did it after that employee is renamed or removed, and a join would
    // quietly rewrite history when they are.
    [MaxLength(40)]
    public string? ActorUserId { get; set; }                // null for system/API actors

    [MaxLength(20)]
    public string? ActorRole { get; set; }

    [MaxLength(200)]
    public string ActorEmail { get; set; } = string.Empty;

    [MaxLength(200)]
    public string ActorName { get; set; } = string.Empty;

    // ---- What happened ----

    // Namespaced "module.verb" — "settings.org.update", "xero.connect". Kept as
    // a code so it stays greppable and filterable; AuditActions maps it to a
    // sentence for the UI.
    [MaxLength(80)]
    public string Action { get; set; } = string.Empty;

    [MaxLength(20)]
    public string Status { get; set; } = AuditStatuses.Success;

    // A sentence an admin can scan without decoding the action code.
    public string Summary { get; set; } = string.Empty;

    // Why it failed. Null on success.
    public string? ErrorReason { get; set; }

    [MaxLength(40)]
    public string? TargetType { get; set; }                 // "Claim", "Project", …

    [MaxLength(40)]
    public string? TargetId { get; set; }

    // Free-form detail, canonical JSON. Hashed, so it cannot be edited after
    // the fact either.
    public string? Metadata { get; set; }

    [MaxLength(64)]
    public string? IpAddress { get; set; }

    // Set EXPLICITLY on write, never left to a database default: the value is
    // part of the hash, and a server-generated timestamp we did not see would
    // make the row unverifiable.
    public DateTime CreatedAt { get; set; }
}

public static class AuditStatuses
{
    public const string Success = "SUCCESS";
    public const string Failed = "FAILED";
}
