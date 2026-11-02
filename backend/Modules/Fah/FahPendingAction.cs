using System.Text.Json;

namespace AltomateHR.Api.Modules.Fah;

// A change the model asked to make, held until the admin decides. Stored on
// the conversation (FahConversation.PendingJson).
//
// Results holds the answers to the other tool calls of the same model turn,
// already run: every call in a turn must be answered together, so they wait
// with it.
//
// Import: an employee import (IFahImportService); Companies: a change in
// several companies (IFahCompanyChanges). Both run in AltomateHR itself on
// Confirm rather than through the MCP server, and their tool call was
// answered when the card was made.
public sealed record FahPendingAction(
    string Id,
    string ToolUseId,
    string Tool,
    JsonElement Arguments,
    string Preview,
    DateTime CreatedAt,
    IReadOnlyList<FahToolAnswer> Results,
    FahImportPlan? Import = null,
    FahCompanyPlan? Companies = null)
{
    public bool AnsweredAtOnce => Import is not null || Companies is not null;
}

public sealed record FahToolAnswer(string ToolUseId, string Text, bool IsError);
