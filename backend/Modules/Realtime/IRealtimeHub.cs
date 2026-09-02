using System.Threading.Channels;
using AltomateHR.Api.Modules.Realtime.Dtos;

namespace AltomateHR.Api.Modules.Realtime;

// The registry of open SSE connections — the only place live connections are
// held, the way a repository is the only place the database is touched.
//
// Registered as a SINGLETON: connections outlive the request that opened them
// (a stream stays open for hours), so this can't be scoped.
public interface IRealtimeHub
{
    // Registers one browser tab. Dispose (via `using`) unregisters it — the
    // controller does this in a finally, so a dropped client never leaks a slot.
    RealtimeConnection Connect(string organizationId, string userId);

    // Fans an event out to every open connection belonging to those users, in
    // that org. Users with no open tab are simply skipped.
    void Publish(string organizationId, IEnumerable<string> userIds, RealtimeEventDto evt);

    // Open connection count, for diagnostics.
    int ConnectionCount { get; }
}

// One browser tab's queue. The controller reads `Events` and writes each one out
// as an SSE frame.
public sealed class RealtimeConnection : IDisposable
{
    private readonly Action _onDispose;

    internal RealtimeConnection(ChannelReader<RealtimeEventDto> events, Action onDispose)
    {
        Events = events;
        _onDispose = onDispose;
    }

    public ChannelReader<RealtimeEventDto> Events { get; }

    public void Dispose() => _onDispose();
}
