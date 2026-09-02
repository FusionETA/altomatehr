namespace AltomateHR.Api.Modules.Leave.Dtos;

// One decision in a leave request's approval chain. Serialised as JSON into
// LeaveApplication.Approvals, because ReviewNotes/DecidedAt only ever hold the
// LAST decision — in a multi-step chain everything before it was lost.
//
// Mirrors production's LeaveApprovalEntry, plus ADMIN_APPLIED for a request an
// admin filed on someone's behalf, so the trail shows who really created it.
public class LeaveApprovalEntryDto
{
    public int Step { get; set; }
    public string ApproverId { get; set; } = string.Empty;

    // APPROVED | REJECTED | ADMIN_APPLIED
    public string Decision { get; set; } = string.Empty;

    public DateTime DecidedAt { get; set; }
    public string? Notes { get; set; }
}
