namespace AltomateHR.Api.Modules.TeamChat;

public interface IBitrixClient
{
    // Posts one message to the configured group chat. Throws on failure; the
    // sender logs it and moves on.
    Task PostAsync(string message, CancellationToken cancellationToken = default);
}
