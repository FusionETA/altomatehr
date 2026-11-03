using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace AltomateHR.Api.Modules.TeamChat;

// Typed HttpClient for Bitrix24's im.message.add, through an inbound webhook.
public class BitrixClient : IBitrixClient
{
    private readonly HttpClient _http;
    private readonly TeamChatOptions _options;

    public BitrixClient(HttpClient http, IOptions<TeamChatOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task PostAsync(string message, CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured) return;

        var url = _options.WebhookUrl.Trim().TrimEnd('/') + "/im.message.add.json";
        // A plain body with its length, not PostAsJsonAsync's chunked stream,
        // which not every webhook endpoint reads.
        var json = JsonSerializer.Serialize(
            new Dictionary<string, string> { ["DIALOG_ID"] = _options.DialogId.Trim(), ["MESSAGE"] = message });
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(url, content, cancellationToken);

        // Bitrix answers 200 with {"error": …} for some refusals (a bad chat
        // id, a webhook without the im scope), so the body is checked too.
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode || body.Contains("\"error\"", StringComparison.Ordinal))
            throw new HttpRequestException(
                $"Bitrix refused the message (HTTP {(int)response.StatusCode}): {body[..Math.Min(body.Length, 200)]}");
    }
}
