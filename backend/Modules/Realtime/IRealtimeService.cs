using AltomateHR.Api.Modules.Realtime.Dtos;

namespace AltomateHR.Api.Modules.Realtime;

// What the rest of the app talks to for live updates. Feature services (claims,
// attendance, leave) call PublishAsync; the SSE controller calls Connect.
//
// Scoped, like every other service — it reads the caller's identity from
// ICurrentUser. The hub it delegates to is the singleton that actually holds the
// open connections.
public interface IRealtimeService
{
    // Opens a stream for the CURRENT caller. Null when the request carries no
    // user or no org claim (the controller answers 401).
    RealtimeConnection? Connect();

    // Nudges the given users that `scope` changed. Best-effort and NEVER throws:
    // a live update is a nicety, and it must not be able to fail the approve /
    // submit / reject that triggered it.
    //
    // `organizationId` is passed EXPLICITLY rather than read from ICurrentUser,
    // because the background sweeps (auto-clock-out, leave accrual) publish with
    // no request context at all — they know the affected row's org, and
    // ICurrentUser would be empty.
    Task PublishAsync(string organizationId, IEnumerable<string?> userIds, RealtimeEventDto evt);

    int ConnectionCount { get; }
}
