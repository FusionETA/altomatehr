using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll.Dtos;

// "Generate split": the admin's words, turned into rules by AI.
public sealed class GenerateXeroSplitDto
{
    [Required, MinLength(5), MaxLength(4000)]
    public string Prompt { get; set; } = string.Empty;

    // The run to try the rules on. Null = the latest run with payslips.
    [MaxLength(40)]
    public string? RunId { get; set; }

    // The back-and-forth after the first prompt, oldest first: the AI's
    // questions and the admin's answers. Empty for a fresh prompt. The client
    // sends only the newest turns, well inside this limit.
    [MaxLength(40)]
    public List<XeroSplitTurnDto> Conversation { get; set; } = [];

    // The rules the AI produced last, so a follow-up refines them rather than
    // starting again. Kept out of the turns so they stay short.
    [MaxLength(50)]
    public List<PayrollJournalSplitRule> CurrentRules { get; set; } = [];
}

public sealed class XeroSplitTurnDto
{
    // "admin" or "ai".
    [Required, RegularExpression("^(admin|ai)$")]
    public string Role { get; set; } = "admin";

    [Required, MaxLength(8000)]
    public string Text { get; set; } = string.Empty;
}

// Something the AI was unsure about, with the likely answers it can see in
// the company's data (accounts, staff…) — shown as one-click choices.
public sealed record XeroSplitQuestion(string Text, IReadOnlyList<string> Options);

// Re-check rules already in hand (saved, or edited) without calling AI.
public sealed class CheckXeroSplitDto
{
    [MaxLength(50)]
    public List<PayrollJournalSplitRule> Rules { get; set; } = [];

    [MaxLength(40)]
    public string? RunId { get; set; }
}

public sealed record XeroSplitGenerateResult(
    bool Ok,
    string? Error,
    IReadOnlyList<PayrollJournalSplitRule> Rules,
    // The AI's one-paragraph reading of the prompt, and anything it was
    // unsure about — shown so the admin can correct the prompt.
    string? Summary,
    IReadOnlyList<XeroSplitQuestion> Questions,
    // "claude" or "gemini".
    string? Provider,
    PayrollXeroSplitPreview? Preview)
{
    public static XeroSplitGenerateResult Refused(string error) =>
        new(false, error, [], null, [], null, null);
}
