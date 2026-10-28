namespace AltomateHR.Api.Modules.Fah;

// FAH, the in-app assistant ("Friendly AltomateHR"). Non-secret settings live in
// appsettings ("Fah"); the Anthropic key is a secret ("Anthropic:ApiKey" in
// user-secrets / the server env), never here.
public class FahOptions
{
    // The AltomateHR MCP server's endpoint, e.g. https://…/mcp. FAH is off while blank.
    public string McpUrl { get; set; } = string.Empty;

    public string Model { get; set; } = "claude-opus-5-5";

    // low | medium | high — how hard the model thinks before answering.
    public string Effort { get; set; } = "medium";

    // A turn stops after this many rounds of tool calls, so a confused turn
    // can't run up cost.
    public int MaxToolRounds { get; set; } = 10;
}
