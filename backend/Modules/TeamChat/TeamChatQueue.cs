using System.Threading.Channels;

namespace AltomateHR.Api.Modules.TeamChat;

// Messages waiting to be posted. A sign-in only drops its line in here and
// moves on; TeamChatSenderBackgroundService posts them. Bounded, so a Bitrix
// outage costs messages rather than memory.
public interface ITeamChatQueue
{
    // False when the queue is full and the message was dropped.
    bool TryEnqueue(string message);

    ChannelReader<string> Reader { get; }
}

public sealed class TeamChatQueue : ITeamChatQueue
{
    public const int Capacity = 1_000;

    private readonly Channel<string> _channel = Channel.CreateBounded<string>(
        new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
        });

    // DropWrite makes TryWrite report success even when it discards the
    // message, so fullness is checked first to be able to say so.
    public bool TryEnqueue(string message) =>
        _channel.Reader.Count < Capacity && _channel.Writer.TryWrite(message);

    public ChannelReader<string> Reader => _channel.Reader;
}
