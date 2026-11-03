using System.Text.Json;

namespace AltomateHR.Api.Modules.Fah;

// Talks to the AltomateHR MCP server: one stateless JSON-RPC POST per call,
// carrying the FAH key of the admin the turn is for.
public interface IMcpClient
{
    // The server's own guidance for the model (its `instructions`).
    Task<string> GetInstructionsAsync(string token, CancellationToken ct);

    Task<IReadOnlyList<McpTool>> ListToolsAsync(string token, CancellationToken ct);

    // A tool-level failure comes back as IsError, not an exception: the model
    // reads the guidance in it and decides what to do next.
    Task<McpToolResult> CallToolAsync(string token, string name, JsonElement arguments, CancellationToken ct);
}

public sealed record McpTool(string Name, string Description, JsonElement InputSchema);

public sealed record McpToolResult(string Text, bool IsError);

// The MCP server couldn't be reached or answered with something unusable.
public sealed class McpUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
