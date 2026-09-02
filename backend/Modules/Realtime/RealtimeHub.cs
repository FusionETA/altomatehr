using System.Collections.Concurrent;
using System.Threading.Channels;
using AltomateHR.Api.Modules.Realtime.Dtos;

namespace AltomateHR.Api.Modules.Realtime;

// In-process fan-out: a dictionary of (org, user) to that person's open tabs.
//
// SINGLE-INSTANCE by design. With more than one API process behind a load
// balancer a publish only reaches the tabs connected to the SAME process, so
// scaling out needs a backplane (Redis pub/sub, as the production monolith
// uses) bridging into Publish. IRealtimeHub is the seam for that: nothing above
// this class knows how the fan-out happens.
public sealed class RealtimeHub : IRealtimeHub
{
    // A slow or wedged client must not be able to grow the server's heap, and
    // these events are "something changed, re-fetch" — the newest one supersedes
    // whatever queued behind it, so dropping the oldest loses nothing.
    private const int PerConnectionQueueDepth = 32;

    // Joined with the unit separator: it can't appear in a GUID, so the org and
    // user halves of a key can never be confused with one another.
    private const string KeySeparator = "\u001f";

    // Keyed by org AND user. User ids are GUIDs, so a cross-tenant collision is
    // implausible — but tenancy is a security boundary, and "implausible" is not
    // the standard this codebase holds elsewhere (see AppDbContext's filters).
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Channel<RealtimeEventDto>>> _connections
        = new(StringComparer.Ordinal);

    // Guards only STRUCTURAL changes (creating/removing a user's bucket).
    // Publishing reads without it, so a fan-out never waits on a connect.
    private readonly object _structureLock = new();

    private readonly ILogger<RealtimeHub> _logger;

    public RealtimeHub(ILogger<RealtimeHub> logger) => _logger = logger;

    public int ConnectionCount => _connections.Values.Sum(bucket => bucket.Count);

    public RealtimeConnection Connect(string organizationId, string userId)
    {
        var key = Key(organizationId, userId);
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<RealtimeEventDto>(new BoundedChannelOptions(PerConnectionQueueDepth)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        lock (_structureLock)
        {
            var bucket = _connections.GetOrAdd(key, _ => new ConcurrentDictionary<Guid, Channel<RealtimeEventDto>>());
            bucket[id] = channel;
        }

        _logger.LogDebug("Realtime connect: user {UserId} in org {OrganizationId}", userId, organizationId);
        return new RealtimeConnection(channel.Reader, () => Disconnect(key, id, channel));
    }

    public void Publish(string organizationId, IEnumerable<string> userIds, RealtimeEventDto evt)
    {
        foreach (var userId in userIds)
        {
            if (!_connections.TryGetValue(Key(organizationId, userId), out var bucket)) continue;

            foreach (var channel in bucket.Values)
            {
                // Bounded + DropOldest, so this only fails on a completed channel
                // (the reader already went away and Dispose hasn't run yet).
                channel.Writer.TryWrite(evt);
            }
        }
    }

    private void Disconnect(string key, Guid id, Channel<RealtimeEventDto> channel)
    {
        lock (_structureLock)
        {
            if (_connections.TryGetValue(key, out var bucket))
            {
                bucket.TryRemove(id, out _);

                // Drop the empty bucket so a long-lived process doesn't accumulate
                // one entry per user who ever connected. Safe under the lock: no
                // Connect can be adding to this bucket concurrently.
                if (bucket.IsEmpty) _connections.TryRemove(key, out _);
            }
        }

        // Ends the controller's read loop if it's still waiting.
        channel.Writer.TryComplete();
    }

    private static string Key(string organizationId, string userId) =>
        string.Concat(organizationId, KeySeparator, userId);
}
