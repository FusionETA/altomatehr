using System.Text.Json;
using System.Text.Json.Serialization;

namespace AltomateHR.Api.Modules.Realtime.Dtos;

// Which surface changed. The client uses this to decide WHAT to re-fetch, so a
// leave decision doesn't cost the browser a claims round-trip.
public enum RealtimeScope
{
    CLAIMS,
    ATTENDANCE,
    LEAVE,
}

// What happened. Deliberately coarse — the payload is a nudge, never the data.
public enum RealtimeAction
{
    SUBMITTED,   // a new request needs someone's review
    UPDATED,     // an existing request was edited
    APPROVED,
    REJECTED,
    CANCELLED,
    DELETED,
}

// The SSE payload. Small on purpose: the client treats it as "this surface
// changed, re-fetch it" and reads the authoritative state back over the normal
// REST endpoints, which already enforce tenancy and role rules.
//
// Sending the changed ENTITY here instead would mean re-implementing those
// visibility rules in the fan-out path — and getting one wrong would push
// another employee's claim into an approver's browser.
public sealed record RealtimeEventDto(
    RealtimeScope Scope,
    RealtimeAction Action,
    string? EntityId,
    DateTime At)
{
    // Enums as strings, camelCase keys — matching what the REST endpoints emit
    // (Program.cs's JsonStringEnumConverter), so the client parses one shape.
    // Held here because SSE frames are serialized by hand, outside MVC.
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static RealtimeEventDto For(RealtimeScope scope, RealtimeAction action, string? entityId = null) =>
        new(scope, action, entityId, DateTime.UtcNow);

    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);
}
