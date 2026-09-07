using AltomateHR.Api.Modules.Approvals.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltomateHR.Api.Modules.Approvals;

[ApiController]
[Route("approvals")]
[Authorize(Roles = "Admin,Owner")]
public class ApprovalsController : ControllerBase
{
    private readonly IApprovalReconciliationService _reconciliation;

    public ApprovalsController(IApprovalReconciliationService reconciliation) =>
        _reconciliation = reconciliation;

    // POST /approvals/reconcile?apply=true — resolve requests that no longer
    // have any approver to route to. Defaults to a DRY RUN: without apply=true
    // it reports what it would resolve and writes nothing.
    //
    // Admin-gated, and that is not the same as an admin approving anything: it
    // applies the system's own "nobody above them" rule to rows that predate it,
    // rather than making a judgement on any individual request. Admins remain
    // outside every approval chain (see OrgRoles).
    [HttpPost("reconcile")]
    public async Task<ActionResult<ApprovalReconciliationDto>> Reconcile([FromQuery] bool apply = false) =>
        Ok(await _reconciliation.RunAsync(apply));
}
