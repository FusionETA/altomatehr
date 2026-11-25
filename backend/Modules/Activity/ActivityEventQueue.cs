using System.Threading.Channels;
using AltomateHR.Api.Modules.Activity.Entities;

namespace AltomateHR.Api.Modules.Activity;

// The hand-off between UserActivityMiddleware and the database — the same shape
// as ApiRequestLogQueue. A request only drops its event in here and moves on;
// ActivityEventWriterBackgroundService saves them in batches. Bounded: a
// database outage costs activity rows, never memory or a request.
public interface IActivityEventQueue
{
    // False when the queue is full and the event was dropped.
    bool TryEnqueue(UserActivityEvent row);

    ChannelReader<UserActivityEvent> Reader { get; }
}

public sealed class ActivityEventQueue : IActivityEventQueue
{
    public const int Capacity = 10_000;

    private readonly Channel<UserActivityEvent> _channel = Channel.CreateBounded<UserActivityEvent>(
        new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
        });

    // DropWrite reports success even when it discards, so fullness is checked first.
    public bool TryEnqueue(UserActivityEvent row) =>
        _channel.Reader.Count < Capacity && _channel.Writer.TryWrite(row);

    public ChannelReader<UserActivityEvent> Reader => _channel.Reader;
}
