using AltomateHR.Api.Modules.Payroll.Dtos;

namespace AltomateHR.Api.Modules.Payroll;

// The AI split's settings-page side: turn a prompt into rules (AI), and
// check rules against a real run (no AI). Posting never comes through here —
// the sync applies the saved rules itself.
public interface IPayrollJournalSplitService
{
    Task<XeroSplitGenerateResult> GenerateAsync(GenerateXeroSplitDto dto, CancellationToken ct = default);

    Task<XeroSplitGenerateResult> CheckAsync(CheckXeroSplitDto dto);
}
