using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Audit.Entities;

namespace AltomateHR.Api.Tests.Audit;

// The chain is the whole point of the audit log: without it, anyone with
// database access can rewrite history silently. These pin down that every kind
// of edit is detected, and that legitimate shapes still verify.
public class AuditChainTests
{
    private static AuditLog Row(int seq, string action = "settings.org.update") => new()
    {
        Id = $"row-{seq}",
        OrganizationId = "org-1",
        Seq = seq,
        ActorUserId = "usr-admin",
        ActorRole = "Admin",
        ActorEmail = "admin@x.com",
        ActorName = "Admin",
        Action = action,
        Status = AuditStatuses.Success,
        Summary = $"Did thing {seq}",
        Metadata = """{"b":2,"a":1}""",
        IpAddress = "10.0.0.1",
        // Fixed, not DateTime.UtcNow: the timestamp is hashed, so a moving one
        // would make these tests unrepeatable.
        CreatedAt = new DateTime(2026, 9, 8, 10, 0, seq, DateTimeKind.Utc),
    };

    // Builds a valid chain the way the repository does.
    private static List<AuditLog> Chain(int length)
    {
        var rows = new List<AuditLog>();
        string? prev = null;
        for (var seq = 1; seq <= length; seq++)
        {
            var row = Row(seq);
            row.PrevHash = prev;
            row.Hash = AuditChain.ComputeHash(row, prev);
            rows.Add(row);
            prev = row.Hash;
        }
        return rows;
    }

    [Fact]
    public void AnUntouchedChainVerifies()
    {
        var rows = Chain(5);

        var result = AuditChain.Verify(rows);

        Assert.True(result.Ok);
        Assert.Equal(5, result.EntriesChecked);
        Assert.Equal(rows[^1].Hash, result.HeadHash);
    }

    [Fact]
    public void AnEmptyChainVerifies()
    {
        var result = AuditChain.Verify([]);

        Assert.True(result.Ok);
        Assert.Equal(0, result.EntriesChecked);
        Assert.Null(result.HeadHash);
    }

    [Fact]
    public void EditingARowIsDetected()
    {
        var rows = Chain(5);
        rows[2].Summary = "Actually I did something else";

        var result = AuditChain.Verify(rows);

        Assert.False(result.Ok);
        Assert.Equal(AuditChainBreak.HashMismatch, result.Reason);
        Assert.Equal(3, result.BrokenAtSeq);
        // Reported at the first break, having verified everything before it.
        Assert.Equal(2, result.EntriesChecked);
    }

    [Theory]
    [InlineData("Action")]
    [InlineData("ActorEmail")]
    [InlineData("Status")]
    [InlineData("TargetId")]
    [InlineData("Metadata")]
    [InlineData("IpAddress")]
    [InlineData("CreatedAt")]
    public void EveryHashedFieldIsCovered(string field)
    {
        // A field that can be edited without breaking the chain is a field an
        // attacker can rewrite freely, so each one is checked explicitly.
        var rows = Chain(3);
        var row = rows[1];

        switch (field)
        {
            case "Action": row.Action = "settings.org.delete"; break;
            case "ActorEmail": row.ActorEmail = "someone.else@x.com"; break;
            case "Status": row.Status = AuditStatuses.Failed; break;
            case "TargetId": row.TargetId = "tampered"; break;
            case "Metadata": row.Metadata = """{"a":99}"""; break;
            case "IpAddress": row.IpAddress = "8.8.8.8"; break;
            case "CreatedAt": row.CreatedAt = row.CreatedAt.AddSeconds(1); break;
        }

        Assert.False(AuditChain.Verify(rows).Ok);
    }

    [Fact]
    public void RelinkingARowIsDetected()
    {
        var rows = Chain(5);
        rows[3].PrevHash = rows[0].Hash;   // re-point it past the row before it

        var result = AuditChain.Verify(rows);

        Assert.False(result.Ok);
        Assert.Equal(AuditChainBreak.PrevHashMismatch, result.Reason);
        Assert.Equal(4, result.BrokenAtSeq);
    }

    [Fact]
    public void DeletingARowFromTheMiddleIsDetected()
    {
        // The case a chain exists for: quietly dropping the one event you did
        // not want anyone to see.
        var rows = Chain(5);
        rows.RemoveAt(2);

        var result = AuditChain.Verify(rows);

        Assert.False(result.Ok);
        Assert.Equal(AuditChainBreak.SequenceGap, result.Reason);
        Assert.Equal(3, result.BrokenAtSeq);
    }

    [Fact]
    public void ARowWithNoHashIsDetected()
    {
        var rows = Chain(3);
        rows[1].Hash = string.Empty;

        Assert.Equal(AuditChainBreak.MissingChainColumns, AuditChain.Verify(rows).Reason);
    }

    [Fact]
    public void AChainTrimmedFromTheFrontStillVerifies()
    {
        // Retention removes a PREFIX. We do not prune today, but verification
        // has to tolerate it or turning retention on later would start
        // reporting every chain as broken.
        var rows = Chain(6).Skip(2).ToList();

        var result = AuditChain.Verify(rows);

        Assert.True(result.Ok);
        Assert.Equal(4, result.EntriesChecked);
    }

    [Fact]
    public void MetadataKeyOrderDoesNotChangeTheHash()
    {
        // Key order is not stable across a database round-trip, so hashing raw
        // serializer output would fail verification for no reason.
        var a = Row(1);
        a.Metadata = """{"alpha":1,"beta":{"x":1,"y":2}}""";
        var b = Row(1);
        b.Metadata = """{"beta":{"y":2,"x":1},"alpha":1}""";

        Assert.Equal(AuditChain.ComputeHash(a, null), AuditChain.ComputeHash(b, null));
    }

    [Fact]
    public void ArrayOrderDoesChangeTheHash()
    {
        // Arrays are ordered data — reordering them IS an edit.
        var a = Row(1);
        a.Metadata = """{"ids":[1,2,3]}""";
        var b = Row(1);
        b.Metadata = """{"ids":[3,2,1]}""";

        Assert.NotEqual(AuditChain.ComputeHash(a, null), AuditChain.ComputeHash(b, null));
    }

    [Fact]
    public void MalformedMetadataDoesNotThrow()
    {
        // A bad metadata blob must not be able to break the chain, or writing
        // one becomes a way to disable auditing.
        var row = Row(1);
        row.Metadata = "{not json at all";

        var hash = AuditChain.ComputeHash(row, null);

        Assert.Equal(64, hash.Length);
    }

    [Fact]
    public void FieldValuesCannotForgeTheFieldBoundary()
    {
        // Fields are newline-joined, so a value containing a newline must not be
        // able to imitate the boundary between two fields and produce the same
        // hash as a differently-split row.
        var a = Row(1);
        a.ActorEmail = "a@x.com";
        a.ActorName = "Admin";
        var b = Row(1);
        b.ActorEmail = "a@x.com\nAdmin";
        b.ActorName = string.Empty;

        Assert.NotEqual(AuditChain.ComputeHash(a, null), AuditChain.ComputeHash(b, null));
    }

    [Fact]
    public void TwoOrganizationsHashDifferentlyAtTheSameSeq()
    {
        // Chains are per-org. Identical events in two tenants must not share a
        // hash, or one org's row could be transplanted into another's chain.
        var a = Row(1);
        var b = Row(1);
        b.OrganizationId = "org-2";

        Assert.NotEqual(AuditChain.ComputeHash(a, null), AuditChain.ComputeHash(b, null));
    }

    [Fact]
    public void ARowSurvivesTheDatabaseRoundTrip()
    {
        // The bug this exists for: the column is datetime(6) but ToString("O")
        // emits seven fractional digits, and a value read back from MySQL comes
        // out Unspecified rather than Utc. Either difference changes the hash,
        // so every row failed to verify while looking untouched.
        var row = Row(1);
        row.CreatedAt = AuditChain.TruncateToStorablePrecision(
            new DateTime(2026, 9, 8, 3, 45, 12, DateTimeKind.Utc).AddTicks(3456789));
        row.Hash = AuditChain.ComputeHash(row, null);

        // What EF hands back: same instant, truncated to the column's six
        // digits, and with the kind lost.
        var readBack = Row(1);
        readBack.CreatedAt = DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Unspecified);
        readBack.Hash = row.Hash;
        readBack.PrevHash = row.PrevHash;

        Assert.True(AuditChain.Verify([readBack]).Ok);
    }

    [Fact]
    public void TruncationDropsOnlyWhatTheColumnCannotHold()
    {
        var value = new DateTime(2026, 9, 8, 3, 45, 12, DateTimeKind.Utc).AddTicks(3456789);

        var truncated = AuditChain.TruncateToStorablePrecision(value);

        // Microsecond precision kept, the sub-microsecond tail dropped.
        Assert.Equal(3456780, truncated.Ticks % TimeSpan.TicksPerSecond);
        Assert.Equal(DateTimeKind.Utc, truncated.Kind);
    }

    [Fact]
    public void TheHeadLineCarriesEnoughToAnchorTheChain()
    {
        var rows = Chain(3);
        var head = AuditChain.FormatHead("org-1", 3, rows[^1].Hash, rows[^1].CreatedAt);

        Assert.Contains("org=org-1", head);
        Assert.Contains("seq=3", head);
        Assert.Contains($"head={rows[^1].Hash}", head);
        Assert.Contains($"format={AuditChain.FormatVersion}", head);
    }
}
