using System.ComponentModel.DataAnnotations;

namespace AltomateHR.Api.Modules.Fah.Dtos;

public class FahChatRequestDto
{
    // Blank starts a new chat.
    [StringLength(40)]
    public string? ConversationId { get; set; }

    // May be blank when files are attached.
    [StringLength(4000)]
    public string? Message { get; set; }

    // Files uploaded for this message (POST /fah/attachments), in order.
    [MaxLength(FahFiles.MaxPerMessage)]
    public List<string>? AttachmentIds { get; set; }
}

// An uploaded file, ready to go with the next message.
public class FahAttachmentDto
{
    public string Id { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    // "image", "pdf" or "text" (a spreadsheet or Word file, read into text).
    public string Kind { get; set; } = string.Empty;
    // e.g. that only the first rows were read.
    public string? Note { get; set; }
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

    // Set for an employee import: the rows, for one Confirm on the whole file.
    public FahImportCardDto? Import { get; set; }

    // Set for a change in several companies: what happens in each.
    public List<FahCompanyCardRowDto>? Companies { get; set; }
}

public class FahCompanyCardRowDto
{
    public string Company { get; set; } = string.Empty;
    // Skipped there (already has it, not theirs to change …) rather than made.
    public bool Skip { get; set; }
    public string Detail { get; set; } = string.Empty;
}

public class FahImportCardDto
{
    public string SourceFile { get; set; } = string.Empty;
    public int Adds { get; set; }
    public int Updates { get; set; }
    // Rows the import would refuse (not imported on Confirm) and rows left
    // out (repeats, or skipped as asked).
    public int Problems { get; set; }
    public int LeftOut { get; set; }
    // The headings of Cells, e.g. Name, Email, Employee No.
    public List<string> Columns { get; set; } = [];
    public List<FahImportCardRowDto> Rows { get; set; } = [];
    // Rows beyond those listed (the download has them all).
    public int MoreRows { get; set; }
}

public class FahImportCardRowDto
{
    // Its row number in the admin's file.
    public int Row { get; set; }
    // "add", "update", "problem" or "leftout".
    public string Status { get; set; } = string.Empty;
    public List<string> Cells { get; set; } = [];
    // Why it won't be imported, or a note on one that will.
    public string? Message { get; set; }
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

    // Its images or PDFs have been deleted (FahFiles.KeepFor), so it can be
    // read but not continued.
    public bool FilesExpired { get; set; }
}

public class FahMessageDto
{
    // "user" or "assistant".
    public string Role { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;

    // The names of files the admin attached to this message.
    public List<string> Attachments { get; set; } = [];
}

// Streamed while a turn runs: a tool starting ("started") or finishing
// ("done", with Ok), so the panel can say what FAH is checking.
public sealed record FahProgressEvent(string Type, string Tool, bool? Ok = null);

public class FahStepDto
{
    public string Tool { get; set; } = string.Empty;
    public bool Ok { get; set; }
}
