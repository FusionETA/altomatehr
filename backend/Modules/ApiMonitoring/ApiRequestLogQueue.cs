using System.Threading.Channels;
using AltomateHR.Api.Modules.ApiMonitoring.Entities;

namespace AltomateHR.Api.Modules.ApiMonitoring;

// The hand-off between the request pipeline and the database. A request only
// ever drops its row in here and moves on; ApiRequestLogWriterBackgroundService
// saves them in batches. Bounded, so a database outage costs log rows rather
// than memory — a full queue drops the new row instead of blocking a request.
public interface IApiRequestLogQueue
{
    // False when the queue is full and the row was dropped.
    bool TryEnqueue(ApiRequestLog row);

    ChannelReader<ApiRequestLog> Reader { get; }
}

public sealed class ApiRequestLogQueue : IApiRequestLogQueue
{
    public const int Capacity = 10_000;

    private readonly Channel<ApiRequestLog> _channel = Channel.CreateBounded<ApiRequestLog>(
        new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
        });

    // DropWrite makes TryWrite report success even when it discards the row,
    // so fullness is checked first to be able to say so.
    public bool TryEnqueue(ApiRequestLog row) =>
        _channel.Reader.Count < Capacity && _channel.Writer.TryWrite(row);

    public ChannelReader<ApiRequestLog> Reader => _channel.Reader;
}
