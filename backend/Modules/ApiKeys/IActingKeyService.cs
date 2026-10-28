namespace AltomateHR.Api.Modules.ApiKeys;

// Short-lived keys that let FAH, the in-app assistant, reach the API through
// the MCP server AS the signed-in admin. The MCP server only takes wp_live_
// keys, so this is how a person's session crosses into it without handing it
// their login.
public interface IActingKeyService
{
    // A fresh key for the caller. FAH makes one per chat turn and ends it when
    // the turn is over, so a key lives for seconds, not the full lifetime.
    // ActingKeyException when the caller can't have one.
    Task<ActingKey> IssueAsync();

    // Ends one key (the turn's). A turn cut off before it can (a crash, a
    // restart) leaves its key until ExpiresAt, at most ActingKeyService.Lifetime.
    Task RevokeAsync(string id);
}

// The raw token never leaves the server: FAH passes it only to the MCP server.
public sealed record ActingKey(string Id, string Token, DateTime ExpiresAt);

public sealed class ActingKeyException(string message) : Exception(message);
