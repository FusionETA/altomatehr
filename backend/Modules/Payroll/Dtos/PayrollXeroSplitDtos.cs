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
}

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
    IReadOnlyList<string> Questions,
    // "claude" or "gemini".
    string? Provider,
    PayrollXeroSplitPreview? Preview)
{
    public static XeroSplitGenerateResult Refused(string error) =>
        new(false, error, [], null, [], null, null);
}
