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
    private readonly IApprovalDigestService _digest;

    public ApprovalsController(IApprovalReconciliationService reconciliation, IApprovalDigestService digest)
    {
        _reconciliation = reconciliation;
        _digest = digest;
    }

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

    // POST /approvals/cron/digest/run — force a digest run now instead of
    // waiting for ApprovalDigestBackgroundService's next 06:00 MYT window.
    // Same shape as POST /attendance/cron/auto-clockout/run and
    // POST /leave/cron/monthly-accrual: an operator/testing escape hatch, not
    // gated by a shared secret since it already requires an Admin/Owner JWT.
    // Runs system-wide (every org) and sends real notifications — there's no
    // dry-run mode, since "what would be sent" is exactly what this returns.
    [HttpPost("cron/digest/run")]
    public async Task<ActionResult<ApprovalDigestRunResultDto>> RunDigest() =>
        Ok(await _digest.RunAsync());
}
