namespace AltomateHR.Api.Modules.Audit.Dtos;

// One row as the activity feed shows it. `Action` is the raw code and `Label`
// the sentence — both, because an admin reads the sentence and an auditor greps
// the code.
public class AuditLogDto
{
    public string Id { get; set; } = string.Empty;
    public int Seq { get; set; }
    public string? ActorUserId { get; set; }
    public string? ActorRole { get; set; }
    public string ActorEmail { get; set; } = string.Empty;
    public string ActorName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? ErrorReason { get; set; }
    public string? TargetType { get; set; }
    public string? TargetId { get; set; }
    public string? IpAddress { get; set; }

    // The detail behind the summary, as raw JSON — "limit 500 → 2000", which
    // layer somebody moved to. Sent through as a string rather than a parsed
    // object: it is free-form per action, and the client only ever renders it.
    public string? Metadata { get; set; }

    public DateTime CreatedAt { get; set; }
}

// Page numbers rather than a cursor. Offset paging is usually a trap — rows
// shifting between requests make a page repeat or skip entries — but this table
// is append-only and ordered by a monotonic per-org Seq, so nothing a reader
// has already passed can move. Total comes back so the UI can say "1-10 of 47".
public class AuditPageDto
{
    public List<AuditLogDto> Entries { get; set; } = new();
    public int Total { get; set; }
}

public class AuditQueryDto
{
    // "xero.connect" exactly, or "xero" for everything in that namespace.
    public string? Action { get; set; }
    public string? Status { get; set; }
    public string? ActorUserId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    public int Limit { get; set; } = 10;

    // 1-based. Anything lower is treated as the first page rather than refused —
    // a bad page number is not worth an error page.
    public int Page { get; set; } = 1;
}

// What a chain check found. `Head` is the line worth publishing outside the
// database — see AuditChain.FormatHead.
public class AuditVerificationDto
{
    public bool Ok { get; set; }
    public int EntriesChecked { get; set; }
    public string? HeadHash { get; set; }
    public string? Head { get; set; }
    public int? BrokenAtSeq { get; set; }
    public string? Reason { get; set; }
}
