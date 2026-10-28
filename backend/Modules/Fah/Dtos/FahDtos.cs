using System.ComponentModel.DataAnnotations;

namespace AltomateHR.Api.Modules.Fah.Dtos;

public class FahChatRequestDto
{
    // Blank starts a new chat.
    [StringLength(40)]
    public string? ConversationId { get; set; }

    [Required, StringLength(4000, MinimumLength = 1)]
    public string Message { get; set; } = string.Empty;
}

public class FahChatResponseDto
{
    public string ConversationId { get; set; } = string.Empty;

    // FAH's answer for this turn.
    public string Reply { get; set; } = string.Empty;

    // A change waiting for the admin's Confirm or Cancel, or null.
    public FahPendingActionDto? PendingAction { get; set; }

    // On a Confirm or Cancel: what happened to the change — "made", "failed",
    // "cancelled", or "unknown" (AltomateHR didn't say). Reported even when
    // FAH's reply after it failed, so the card never says "not made" wrongly.
    public string? ChangeOutcome { get; set; }

    // What FAH looked up along the way, in order ("list_employees"), for the
    // "Checking employees…" lines in the chat.
    public List<FahStepDto> Steps { get; set; } = [];
}

// The card: AltomateHR's own preview of the change, not the model's account of it.
public class FahPendingActionDto
{
    public string Id { get; set; } = string.Empty;
    public string Tool { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Preview { get; set; } = string.Empty;
}

public class FahConfirmRequestDto
{
    [Required, StringLength(40)]
    public string ConversationId { get; set; } = string.Empty;

    [Required, StringLength(40)]
    public string ActionId { get; set; } = string.Empty;

    // True = Confirm, false = Cancel.
    public bool Approve { get; set; }
}

public class FahStatusDto
{
    // FAH is set up on this server (model key and MCP server both configured).
    public bool Available { get; set; }
}

public class FahConversationSummaryDto
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}

// A chat as the panel shows it: the admin's messages and FAH's answers, not
// the tool calls and results between them.
public class FahConversationDto
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public List<FahMessageDto> Messages { get; set; } = [];
    public FahPendingActionDto? PendingAction { get; set; }
}

public class FahMessageDto
{
    // "user" or "assistant".
    public string Role { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}

// Streamed while a turn runs: a tool starting ("started") or finishing
// ("done", with Ok), so the panel can say what FAH is checking.
public sealed record FahProgressEvent(string Type, string Tool, bool? Ok = null);

public class FahStepDto
{
    public string Tool { get; set; } = string.Empty;
    public bool Ok { get; set; }
}
