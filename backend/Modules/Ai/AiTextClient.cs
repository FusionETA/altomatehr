using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;

namespace AltomateHR.Api.Modules.Ai;

// Claude first, Gemini as the fallback.
//
// Claude is used when `Anthropic:ApiKey` is set (a secret: user-secrets or the
// server env, never appsettings). Gemini (`Gemini:ApiKey`) takes over when
// Claude is not configured, unreachable, errors, or declines the request —
// so the feature keeps working through an outage of either one.
public class AiTextClient : IAiTextClient
{
    private const string DefaultModel = "claude-opus-5-5";

    private readonly IGeminiClient _gemini;
    private readonly ILogger<AiTextClient> _log;
    private readonly string? _anthropicKey;
    private readonly string _model;

    public AiTextClient(IGeminiClient gemini, IConfiguration configuration, ILogger<AiTextClient> log)
    {
        _gemini = gemini;
        _log = log;
        _anthropicKey = configuration["Anthropic:ApiKey"];
        _model = configuration["Ai:Model"] is { Length: > 0 } model ? model : DefaultModel;
    }

    public async Task<AiTextResult> GenerateJsonAsync(
        string systemInstruction, string userText, CancellationToken cancellationToken = default)
    {
        Exception? claudeFailure = null;

        if (!string.IsNullOrWhiteSpace(_anthropicKey))
        {
            try
            {
                return new AiTextResult(await ClaudeAsync(systemInstruction, userText, cancellationToken), "claude");
            }
            catch (Exception ex) when (ex is AnthropicException or AiProviderException)
            {
                claudeFailure = ex;
                _log.LogWarning(ex, "Claude request failed; falling back to Gemini.");
            }
        }

        try
        {
            var text = await _gemini.GenerateTextAsync(
                systemInstruction,
                [new GeminiTurn("user", userText)],
                new GeminiTextOptions(MaxOutputTokens: 8192, Temperature: 0.2, Json: true),
                cancellationToken);
            return new AiTextResult(text, "gemini");
        }
        catch (AiConfigurationException) when (claudeFailure is not null)
        {
            // Gemini isn't set up either: the Claude error is the useful one.
            throw new AiProviderException("The AI service couldn't answer just now. Try again in a moment.", claudeFailure);
        }
        catch (AiConfigurationException)
        {
            throw new AiConfigurationException(
                "AI isn't set up: set Anthropic:ApiKey (or Gemini:ApiKey as the fallback).");
        }
    }

    private async Task<string> ClaudeAsync(string system, string userText, CancellationToken ct)
    {
        var client = new AnthropicClient { ApiKey = _anthropicKey! };

        var parameters = new MessageCreateParams
        {
            Model = _model,
            MaxTokens = 16000,
            System = system,
            OutputConfig = new BetaOutputConfig { Effort = "medium" },
            // A safety false positive is retried by the API on a fallback model.
            Betas = ["server-side-fallback-2026-07-01"],
            Fallbacks = new Default(),
            Messages =
            [
                JsonSerializer.Deserialize<BetaMessageParam>(
                    JsonSerializer.Serialize(new { role = "user", content = userText }))!,
            ],
        };

        var response = await client.Beta.Messages.Create(parameters, cancellationToken: ct);

        var stop = response.StopReason?.ToString() ?? string.Empty;
        if (stop.Contains("refusal", StringComparison.OrdinalIgnoreCase))
            throw new AiProviderException("Claude declined the request.");

        var text = string.Join("", response.Content
            .Select(b => b.TryPickText(out BetaTextBlock? t) ? t.Text : null)
            .Where(t => t is not null));

        if (string.IsNullOrWhiteSpace(text))
            throw new AiProviderException("Claude returned an empty answer.");

        return text;
    }
}
