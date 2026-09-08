using AltomateHR.Api.Modules.Approvals.Dtos;

namespace AltomateHR.Api.Modules.Approvals;

// The once-daily "here's everything waiting on you" summary, replacing
// Attendance's old per-submission notification (Claims/Leave/Overtime still
// notify immediately on submit — only Attendance's per-request notification
// was noisy enough to move here, and moving it meant the digest itself might
// as well cover every module a reviewer could have pending work in).
//
// Asks each module for its own current pending-approval count per reviewer
// (same "ask each module for one number and combine them" shape as
// IApprovalReconciliationService) and sends one combined notification per
// reviewer via INotificationService — never per module.
public interface IApprovalDigestService
{
    Task<ApprovalDigestRunResultDto> RunAsync();
}
