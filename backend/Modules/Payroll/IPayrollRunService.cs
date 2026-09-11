using AltomateHR.Api.Modules.Payroll.Dtos;

namespace AltomateHR.Api.Modules.Payroll;

// Null Run/Result with a null Error means "not found"; a non-null Error is a
// rule the caller broke. Mirrors PolicySaveResult next door.
public record PayrollRunSaveResult(bool Ok, PayrollRunDto? Run, string? Error);

public record PayrollRunGenerateResult(
    bool Ok, GeneratePayrollRunResultDto? Result, string? Error);

public interface IPayrollRunService
{
    Task<List<PayrollRunDto>> GetAllAsync();

    // The run and its payslips. Null → no such run in this org.
    Task<PayrollRunDetailDto?> GetAsync(string id);

    // Start a run for a period. There can only be one per period, so a second
    // attempt is a conflict rather than a second run.
    Task<PayrollRunSaveResult> CreateAsync(CreatePayrollRunDto dto);

    // Build every payslip on the run from scratch. Idempotent by design: the
    // previous payslips and line items are discarded, not merged into.
    Task<PayrollRunGenerateResult> GenerateAsync(string id);

    // ---- The status machine ----
    //
    // DRAFT ──submit──▶ PENDING_APPROVAL ──approve──▶ SUBMITTED
    //   ▲                      │                          │
    //   └───────reject─────────┘                          │
    //   └──────────────────revert──────────────────────────┘
    //
    // Two steps and two actors on purpose: the person who proposes a month's
    // pay and the person who puts it live are different questions to an
    // auditor. SUBMITTED is what feeds every later run's YTD, which is why
    // getting into it is guarded and getting out of it cascades.

    // DRAFT → PENDING_APPROVAL. Guarded — see the implementation; most of the
    // value of this phase is in what it refuses.
    Task<PayrollRunSaveResult> SubmitForApprovalAsync(string id);

    // PENDING_APPROVAL → SUBMITTED.
    Task<PayrollRunSaveResult> ApproveAsync(string id);

    // PENDING_APPROVAL → DRAFT, with a reason for the submitter.
    Task<PayrollRunSaveResult> RejectAsync(string id, string? reason);

    // SUBMITTED → DRAFT, CASCADING to every later submitted month in the same
    // year. Their YTD-cumulative figures depend on this one.
    Task<PayrollRunRevertResult> RevertToDraftAsync(string id);

    // Which months a revert of this run would also pull back to draft, so the
    // admin is told before confirming rather than after. Empty is the common case.
    Task<IReadOnlyList<string>> GetRevertImpactAsync(string id);

    // Delete a DRAFT run and everything hanging off it.
    Task<PayrollRunSaveResult> DeleteDraftAsync(string id);
}

// Revert reports the whole cascade, not just the run asked for — an admin who
// reverted January needs to see that February and March came back too.
public record PayrollRunRevertResult(
    bool Ok, PayrollRunDto? Run, IReadOnlyList<string> AlsoReverted, string? Error);
