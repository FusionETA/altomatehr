using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AltomateHR.Api.Modules.Audit.Entities;

namespace AltomateHR.Api.Modules.Audit;

// Tamper-evident hash chaining for the audit log.
//
// Every row carries Seq (per-organization, 1-based), Hash and PrevHash. A row's
// hash covers its own fields AND the previous row's hash, so editing row N
// invalidates every row after it — you cannot quietly rewrite one entry, only
// the whole tail, and rewriting the tail is exactly the kind of bulk change an
// auditor can look for.
//
// TAMPER-EVIDENT, NOT TAMPER-PROOF. Anyone with write access to the database can
// recompute a whole chain and leave it internally consistent. What defeats that
// is publishing the head hash somewhere you do not control — see FormatHead.
//
// The chain is per organization, matching how every other query here is scoped.
// Two orgs write independent chains and never block each other.
public static class AuditChain
{
    // Part of the hashed input. Bump it if the set of hashed fields ever
    // changes: old rows keep verifying under the version they were written
    // with, so history stays checkable across a format change.
    public const string FormatVersion = "v1";

    // Hashed in place of PrevHash for the first entry in a chain.
    private const string GenesisPrevHash = "0000000000000000000000000000000000000000000000000000000000000000";

    // Deterministic JSON: object keys sorted at every level, so two semantically
    // identical metadata values always hash the same. Raw serializer output
    // preserves insertion order, which is not stable across a database
    // round-trip — hashing it would produce spurious verification failures.
    public static string CanonicalJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return "null";

        try
        {
            using var doc = JsonDocument.Parse(json);
            var builder = new StringBuilder();
            WriteCanonical(doc.RootElement, builder);
            return builder.ToString();
        }
        catch (JsonException)
        {
            // Not JSON at all — hash the raw text rather than throwing. A
            // malformed metadata blob must not be able to break the chain.
            return JsonSerializer.Serialize(json);
        }
    }

    private static void WriteCanonical(JsonElement element, StringBuilder builder)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                builder.Append('{');
                var first = true;
                foreach (var property in element.EnumerateObject()
                             .OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    if (!first) builder.Append(',');
                    builder.Append(JsonSerializer.Serialize(property.Name)).Append(':');
                    WriteCanonical(property.Value, builder);
                    first = false;
                }
                builder.Append('}');
                break;

            case JsonValueKind.Array:
                builder.Append('[');
                var firstItem = true;
                foreach (var item in element.EnumerateArray())
                {
                    if (!firstItem) builder.Append(',');
                    WriteCanonical(item, builder);
                    firstItem = false;
                }
                builder.Append(']');
                break;

            default:
                builder.Append(element.GetRawText());
                break;
        }
    }

    // Newline-delimited so no field value can be crafted to imitate the boundary
    // between two fields: every component is either a fixed-shape primitive or
    // JSON-quoted.
    private static string Serialize(AuditLog entry, string prevHashHex) =>
        string.Join('\n', [
            FormatVersion,
            entry.OrganizationId,
            entry.Seq.ToString(),
            entry.Action,
            entry.Status,
            entry.ActorUserId ?? string.Empty,
            entry.ActorEmail,
            entry.ActorName,
            entry.TargetType ?? string.Empty,
            entry.TargetId ?? string.Empty,
            JsonSerializer.Serialize(entry.Summary),
            JsonSerializer.Serialize(entry.ErrorReason ?? string.Empty),
            CanonicalJson(entry.Metadata),
            entry.IpAddress ?? string.Empty,
            FormatTimestamp(entry.CreatedAt),
            prevHashHex,
        ]);

    // The timestamp exactly as the database will hand it back, or nothing
    // verifies. Two round-trip hazards, both learned the hard way:
    //
    //   PRECISION — the column is datetime(6), six fractional digits, but
    //   ToString("O") emits seven. The seventh is dropped on the way in, so a
    //   row hashed with it can never be reproduced from what was stored.
    //
    //   KIND — a DateTime read back from MySQL comes out Unspecified, not Utc,
    //   and "O" renders those differently (no trailing Z). SpecifyKind, NOT
    //   ToUniversalTime: the value already IS UTC, and converting an
    //   Unspecified one would shift it by the server's offset.
    //
    // AuditService truncates to the same six digits before saving, so the
    // hashed value and the stored value are identical from the start rather
    // than relying on how MySQL rounds.
    private static string FormatTimestamp(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc)
            .ToString("yyyy-MM-ddTHH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);

    // Drops anything below the database column's resolution.
    public static DateTime TruncateToStorablePrecision(DateTime value) =>
        new(value.Ticks - value.Ticks % (TimeSpan.TicksPerMillisecond / 1000), value.Kind);

    // SHA-256 over the canonical serialization, as lowercase hex. A null
    // prevHash means this is the chain's first entry and hashes as 64 zeroes.
    public static string ComputeHash(AuditLog entry, string? prevHash)
    {
        var payload = Serialize(entry, string.IsNullOrEmpty(prevHash) ? GenesisPrevHash : prevHash);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    // Walk a chain in Seq order and recompute every hash. Rows are passed in
    // rather than fetched here so this stays free of EF and unit-testable.
    //
    // Returns the FIRST break found, with the seq that failed and why. Retention
    // interacts with this: trimming old rows removes a PREFIX, so a trimmed
    // chain legitimately starts at some seq > 1 and its first surviving row
    // points at a row that no longer exists. Verification therefore starts from
    // whatever the first row's seq is and does not check that row's PrevHash —
    // there is nothing left to check it against. A gap anywhere after that is a
    // genuine break.
    //
    // We do not prune today, but writing it this way costs nothing and means
    // adding retention later cannot silently start reporting false breaks.
    public static AuditChainVerification Verify(IReadOnlyList<AuditLog> rows)
    {
        if (rows.Count == 0) return AuditChainVerification.Intact(0, null);

        string? previousHash = null;
        var expectedSeq = rows[0].Seq;
        var isFirstRow = true;
        var verified = 0;

        foreach (var row in rows)
        {
            if (string.IsNullOrEmpty(row.Hash))
                return AuditChainVerification.Broken(verified, row.Seq, AuditChainBreak.MissingChainColumns);

            if (row.Seq != expectedSeq)
                return AuditChainVerification.Broken(verified, expectedSeq, AuditChainBreak.SequenceGap);

            if (isFirstRow)
            {
                // Its predecessor may have been trimmed, so trust its stored
                // PrevHash and use it to seed the walk.
                previousHash = row.PrevHash;
                isFirstRow = false;
            }
            else if (!string.Equals(row.PrevHash, previousHash, StringComparison.Ordinal))
            {
                return AuditChainVerification.Broken(verified, row.Seq, AuditChainBreak.PrevHashMismatch);
            }

            if (!string.Equals(ComputeHash(row, previousHash), row.Hash, StringComparison.Ordinal))
                return AuditChainVerification.Broken(verified, row.Seq, AuditChainBreak.HashMismatch);

            previousHash = row.Hash;
            expectedSeq += 1;
            verified += 1;
        }

        return AuditChainVerification.Intact(verified, previousHash);
    }

    // One line describing a chain head, suitable for publishing OUTSIDE the
    // database — emailing to a client, committing to git, printing on a monthly
    // report. An anchored head is what makes the chain evidence rather than just
    // a log: history before the anchor can no longer be rewritten undetectably,
    // even by someone with full database access.
    public static string FormatHead(string organizationId, int seq, string hash, DateTime asOf) =>
        $"org={organizationId} seq={seq} head={hash} as_of={asOf:O} format={FormatVersion}";
}

public enum AuditChainBreak
{
    None,
    MissingChainColumns,
    SequenceGap,
    PrevHashMismatch,
    HashMismatch,
}

public sealed record AuditChainVerification(
    bool Ok,
    int EntriesChecked,
    string? HeadHash,
    int? BrokenAtSeq,
    AuditChainBreak Reason)
{
    // Named Intact, not Ok: `Ok` is already the record's own property.
    public static AuditChainVerification Intact(int checkedCount, string? head) =>
        new(true, checkedCount, head, null, AuditChainBreak.None);

    public static AuditChainVerification Broken(int checkedCount, int seq, AuditChainBreak reason) =>
        new(false, checkedCount, null, seq, reason);
}
