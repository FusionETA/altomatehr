using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;
using Microsoft.Extensions.Options;

namespace AltomateHR.Api.Modules.Fah;

// FAH on Claude, through the official Anthropic SDK. The only class that knows
// the SDK: everything else speaks IFahModel.
public class ClaudeFahModel : IFahModel
{
    private readonly FahOptions _options;
    private readonly string? _apiKey;

    public ClaudeFahModel(IOptions<FahOptions> options, IConfiguration configuration)
    {
        _options = options.Value;
        _apiKey = configuration["Anthropic:ApiKey"];
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public async Task<FahModelTurn> NextAsync(
        string system,
        IReadOnlyList<FahTool> tools,
        IReadOnlyList<JsonElement> messages,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new FahModelException("FAH isn't set up: Anthropic:ApiKey is missing.");

        var client = new AnthropicClient { ApiKey = _apiKey };
        var parameters = new MessageCreateParams
        {
            Model = _options.Model,
            MaxTokens = 16000,
            System = system,
            OutputConfig = new BetaOutputConfig { Effort = _options.Effort },
            // The tool list and instructions are the same every call, and the
            // conversation only grows: cache it all, so each call re-reads it cheaply.
            CacheControl = new BetaCacheControlEphemeral(),
            // If the model declines on a safety false positive, the API retries
            // the same request on a fallback model it picks.
            Betas = ["server-side-fallback-2026-07-01"],
            Fallbacks = new Default(),
            Tools = [.. tools.Select(t => (BetaToolUnion)new BetaTool
            {
                Name = t.Name,
                Description = t.Description,
                InputSchema = JsonSerializer.Deserialize<InputSchema>(t.InputSchema.GetRawText())!,
            })],
            Messages = [.. messages.Select(m => JsonSerializer.Deserialize<BetaMessageParam>(m.GetRawText())!)],
        };

        BetaMessage response;
        try
        {
            response = await client.Beta.Messages.Create(parameters, cancellationToken: ct);
        }
        catch (AnthropicApiException ex)
        {
            throw new FahModelException("FAH couldn't reach its AI service just now. Try again in a moment.", ex);
        }

        var content = response.Content.Select(b => JsonSerializer.SerializeToElement(b)).ToList();
        var assistant = JsonSerializer.SerializeToElement(new { role = "assistant", content });

        var toolUses = new List<FahToolUse>();
        var text = new List<string>();
        foreach (var block in response.Content)
        {
            if (block.TryPickToolUse(out BetaToolUseBlock? use))
                toolUses.Add(new FahToolUse(use.ID, use.Name, JsonSerializer.SerializeToElement(use.Input)));
            else if (block.TryPickText(out BetaTextBlock? t))
                text.Add(t.Text);
        }

        return new FahModelTurn(
            assistant,
            response.StopReason?.ToString() ?? string.Empty,
            toolUses,
            string.Join("\n\n", text),
            response.Usage.InputTokens,
            response.Usage.OutputTokens,
            response.Usage.CacheCreationInputTokens ?? 0,
            response.Usage.CacheReadInputTokens ?? 0);
    }
}
