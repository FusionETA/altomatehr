using AltomateHR.Api.Modules.Audit.Dtos;

namespace AltomateHR.Api.Modules.Audit;

// What a caller passes when recording an event. Only Action and Summary are
// really required — everything else is either resolved from the current user or
// genuinely optional.
public sealed record AuditEvent(
    string Action,
    string Summary,
    string? TargetType = null,
    string? TargetId = null,
    object? Metadata = null,
    string Status = "SUCCESS",
    string? ErrorReason = null,
    // Only for events with no signed-in user — a failed sign-in knows the org
    // from the email it was given, not from a token.
    string? OrganizationId = null,
    string? ActorEmail = null,
    string? ActorName = null);

public interface IAuditService
{
    // Record an event. NEVER THROWS: an audit failure must not take down the
    // action that triggered it. Callers should not wrap this in try/catch.
    Task WriteAsync(AuditEvent entry);

    Task<AuditPageDto> ListAsync(AuditQueryDto query);

    // Recompute the org's whole chain. Admin-only — it is the answer to "has
    // anyone edited this log?".
    Task<AuditVerificationDto> VerifyAsync();
}
