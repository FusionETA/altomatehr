using System.Threading.Channels;

namespace AltomateHR.Api.Modules.TeamChat;

public sealed record TeamChatMessage(TeamChatChannel Channel, string Text);

// Messages waiting to be posted. A sign-in only drops its line in here and
// moves on; TeamChatSenderBackgroundService posts them. Bounded, so a Bitrix
// outage costs messages rather than memory.
public interface ITeamChatQueue
{
    // False when the queue is full and the message was dropped.
    bool TryEnqueue(TeamChatMessage message);

    ChannelReader<TeamChatMessage> Reader { get; }
}

public sealed class TeamChatQueue : ITeamChatQueue
{
    public const int Capacity = 1_000;

    private readonly Channel<TeamChatMessage> _channel = Channel.CreateBounded<TeamChatMessage>(
        new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
        });

    // DropWrite makes TryWrite report success even when it discards the
    // message, so fullness is checked first to be able to say so.
    public bool TryEnqueue(TeamChatMessage message) =>
        _channel.Reader.Count < Capacity && _channel.Writer.TryWrite(message);

    public ChannelReader<TeamChatMessage> Reader => _channel.Reader;
}
