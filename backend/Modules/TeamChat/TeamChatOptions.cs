namespace AltomateHR.Api.Modules.TeamChat;

// Where team alerts go: a Bitrix24 group chat, posted to through an inbound
// webhook (the same way WazzOCR posts its error alerts).
//
// WebhookUrl carries Bitrix's token, so it's a secret: user-secrets locally,
// an environment variable (Bitrix__WebhookUrl) on the server. Either one
// empty means nothing is posted — so a dev machine stays quiet by default.
public class TeamChatOptions
{
    public const string SectionName = "Bitrix";

    // The webhook base, e.g. https://x.bitrix24.com/rest/1/abc123/
    public string WebhookUrl { get; set; } = string.Empty;

    // The group chat to post into, e.g. "chat1234".
    public string DialogId { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(WebhookUrl) && !string.IsNullOrWhiteSpace(DialogId);
}
