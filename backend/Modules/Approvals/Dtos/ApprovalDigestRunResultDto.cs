namespace AltomateHR.Api.Modules.Approvals.Dtos;

// One reviewer's combined pending-approval count across all four modules,
// plus the per-module breakdown that produced it — always all four keys
// (CLAIMS, LEAVE, ATTENDANCE, OT), even when a module's own count is zero, so
// a reviewer sees the full shape of their backlog rather than only the parts
// of it that are non-empty.
public record ApprovalDigestEntryDto(
    string ReviewerId,
    string OrganizationId,
    int TotalPendingCount,
    IReadOnlyDictionary<string, int> ByModule);

// What one digest run sent. `ReviewersNotified` is just Entries.Count, kept
// as its own field so the manual-trigger endpoint's response reads clearly
// without the caller having to count the list themselves.
public record ApprovalDigestRunResultDto(
    int ReviewersNotified,
    IReadOnlyList<ApprovalDigestEntryDto> Entries);
