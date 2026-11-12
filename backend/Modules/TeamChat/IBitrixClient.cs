namespace AltomateHR.Api.Modules.TeamChat;

public interface IBitrixClient
{
    // Posts one message to the channel's group chat; nothing when that
    // channel has no chat. Throws on failure; the sender logs it and moves on.
    Task PostAsync(TeamChatChannel channel, string message, CancellationToken cancellationToken = default);
}
