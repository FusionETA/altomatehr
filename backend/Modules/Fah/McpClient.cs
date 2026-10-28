using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace AltomateHR.Api.Modules.Fah;

public class McpClient : IMcpClient
{
    private readonly HttpClient _http;
    private readonly FahOptions _options;
    private int _nextId;

    public McpClient(HttpClient http, IOptions<FahOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task<string> GetInstructionsAsync(string token, CancellationToken ct)
    {
        var result = await RpcAsync(token, "initialize", new
        {
            protocolVersion = "2025-06-18",
            capabilities = new { },
            clientInfo = new { name = "altomatehr-fah", version = "1.0" },
        }, ct);
        return result.TryGetProperty("instructions", out var i) && i.ValueKind == JsonValueKind.String
            ? i.GetString() ?? string.Empty
            : string.Empty;
    }

    public async Task<IReadOnlyList<McpTool>> ListToolsAsync(string token, CancellationToken ct)
    {
        var result = await RpcAsync(token, "tools/list", new { }, ct);
        if (!result.TryGetProperty("tools", out var tools) || tools.ValueKind != JsonValueKind.Array)
            throw new McpUnavailableException("The MCP server sent no tool list.");

        return [.. tools.EnumerateArray().Select(t => new McpTool(
            t.GetProperty("name").GetString() ?? string.Empty,
            t.TryGetProperty("description", out var d) ? d.GetString() ?? string.Empty : string.Empty,
            t.TryGetProperty("inputSchema", out var s) ? s.Clone() : JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone()))];
    }

    public async Task<McpToolResult> CallToolAsync(string token, string name, JsonElement arguments, CancellationToken ct)
    {
        var result = await RpcAsync(token, "tools/call", new { name, arguments }, ct);
        var text = new StringBuilder();
        if (result.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var type) && type.GetString() == "text"
                    && block.TryGetProperty("text", out var t))
                {
                    if (text.Length > 0) text.Append('\n');
                    text.Append(t.GetString());
                }
            }
        }
        var isError = result.TryGetProperty("isError", out var e) && e.ValueKind == JsonValueKind.True;
        return new McpToolResult(text.ToString(), isError);
    }

    private async Task<JsonElement> RpcAsync(string token, string method, object parameters, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.McpUrl))
            throw new McpUnavailableException("FAH isn't set up: Fah:McpUrl is blank.");

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.McpUrl)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { jsonrpc = "2.0", id = Interlocked.Increment(ref _nextId), method, @params = parameters }),
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");

        string body;
        try
        {
            using var response = await _http.SendAsync(request, ct);
            body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new McpUnavailableException($"The MCP server answered HTTP {(int)response.StatusCode}.");
        }
        catch (HttpRequestException ex)
        {
            throw new McpUnavailableException("The MCP server couldn't be reached.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // HttpClient.Timeout ran out. The call may still have landed.
            throw new McpUnavailableException("The MCP server took too long to answer.", ex);
        }

        JsonElement envelope;
        try
        {
            envelope = JsonDocument.Parse(DataOf(body)).RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new McpUnavailableException("The MCP server sent something that isn't JSON.", ex);
        }

        if (envelope.TryGetProperty("error", out var error))
        {
            var message = error.TryGetProperty("message", out var m) ? m.GetString() : null;
            throw new McpUnavailableException($"The MCP server refused {method}: {message ?? "no reason given"}.");
        }
        return envelope.TryGetProperty("result", out var result)
            ? result
            : throw new McpUnavailableException($"The MCP server sent no result for {method}.");
    }

    // An older-protocol reply comes framed as one SSE event; a current one is plain JSON.
    private static string DataOf(string body)
    {
        if (!body.StartsWith("event:", StringComparison.Ordinal)) return body;
        var data = body.Split('\n').FirstOrDefault(l => l.StartsWith("data: ", StringComparison.Ordinal));
        return data is null ? body : data["data: ".Length..];
    }
}
