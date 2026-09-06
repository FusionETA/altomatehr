using AltomateHR.Api.Modules.Approvals.Dtos;

namespace AltomateHR.Api.Modules.Approvals;

// Finds and resolves requests that can never be approved.
//
// Every module decides a submission immediately when the applicant has nobody
// above them, but that rule only runs at submit. Rows written earlier can be
// left unreachable when the hierarchy changes underneath them — removing admins
// from it (see OrgRoles) deleted the step that requests were parked on, and
// reassigning or offboarding a supervisor does the same. An unreachable request
// shows up in no approval queue and is refused for every caller who tries to
// decide it: it is stuck, not pending, and nothing else in the system will ever
// clear it.
//
// Deliberately NOT automatic. A sweep on every startup would auto-approve
// everything during a mid-edit moment when a team briefly has no approvers —
// resolving a request is not something to do on a transient reading of the org
// chart. So this is an explicit administrative action, with a dry run first.
public interface IApprovalReconciliationService
{
    Task<ApprovalReconciliationDto> RunAsync(bool apply);
}
