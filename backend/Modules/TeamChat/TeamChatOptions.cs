namespace AltomateHR.Api.Modules.TeamChat;

// Which of the team's three Bitrix chats a message goes to.
public enum TeamChatChannel
{
    // What customers did: new companies, payroll approved, leave and claims
    // decided, attendance rejected.
    Action,

    // What the team may need to act on: sign-ins, repeated failed sign-ins,
    // a portal password viewed, Xero needing reconnecting, payslip emails
    // failing, payroll runs deleted or reverted.
    Alert,

    // The 08:00 summary of yesterday, company by company.
    Daily,
}

// Where team alerts go: Bitrix24 group chats, posted to through one inbound
// webhook (the same way WazzOCR posts its error alerts).
//
// WebhookUrl carries Bitrix's token, so it's a secret: user-secrets locally,
// an environment variable (Bitrix__WebhookUrl) on the server. Each chat has
// its own id (Bitrix__ActionDialogId, Bitrix__AlertDialogId,
// Bitrix__DailyDialogId); one left empty falls back to Bitrix__DialogId, the
// single chat everything went to before. No webhook, or no chat for a
// channel, means nothing is posted there — so a dev machine stays quiet.
public class TeamChatOptions
{
    public const string SectionName = "Bitrix";

    // The webhook base, e.g. https://x.bitrix24.com/rest/1/abc123/
    public string WebhookUrl { get; set; } = string.Empty;

    // The fallback chat, e.g. "chat1234".
    public string DialogId { get; set; } = string.Empty;

    public string ActionDialogId { get; set; } = string.Empty;
    public string AlertDialogId { get; set; } = string.Empty;
    public string DailyDialogId { get; set; } = string.Empty;

    // The chat a channel posts to, or null when it posts nowhere.
    public string? DialogFor(TeamChatChannel channel)
    {
        if (string.IsNullOrWhiteSpace(WebhookUrl)) return null;
        var own = channel switch
        {
            TeamChatChannel.Action => ActionDialogId,
            TeamChatChannel.Alert => AlertDialogId,
            TeamChatChannel.Daily => DailyDialogId,
            _ => string.Empty,
        };
        var dialog = string.IsNullOrWhiteSpace(own) ? DialogId : own;
        return string.IsNullOrWhiteSpace(dialog) ? null : dialog.Trim();
    }

    public bool IsConfigured(TeamChatChannel channel) => DialogFor(channel) is not null;
}
