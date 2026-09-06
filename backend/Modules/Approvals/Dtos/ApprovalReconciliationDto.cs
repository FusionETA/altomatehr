namespace AltomateHR.Api.Modules.Approvals.Dtos;

// What a reconciliation run found, per module and in total.
//
// `Applied` is false for a dry run — the counts are then "what WOULD be
// resolved", so the damage can be inspected before anything is written.
public record ApprovalReconciliationDto(
    bool Applied,
    int Total,
    IReadOnlyDictionary<string, int> ByModule);
