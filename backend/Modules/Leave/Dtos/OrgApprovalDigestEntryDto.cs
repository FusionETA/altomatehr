namespace AltomateHR.Api.Modules.Leave.Dtos;

// One reviewer's pending-leave count, org-wide (every org, since the caller —
// Modules/Approvals/ApprovalDigestService — runs with no request context and
// spans every tenant). Mirrors Attendance's OrgApprovalDigestEntryDto; kept as
// Leave's own copy rather than a shared type, matching how each module already
// owns its own status enum.
public class OrgApprovalDigestEntryDto
{
    public string ReviewerId { get; set; } = string.Empty;
    public string OrganizationId { get; set; } = string.Empty;
    public int PendingCount { get; set; }
}
