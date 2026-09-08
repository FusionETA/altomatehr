using AltomateHR.Api.Modules.Audit.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltomateHR.Api.Modules.Audit;

[ApiController]
[Route("audit")]
// Admin-only throughout: the log records who changed the org's configuration,
// which is oversight, not something an employee needs.
[Authorize(Roles = "Admin,Owner")]
public class AuditController : ControllerBase
{
    private readonly IAuditService _audit;

    public AuditController(IAuditService audit) => _audit = audit;

    // GET /audit — the activity feed, newest first. Cursor-paged: pass the
    // previous page's nextCursor to get the next, older, page.
    [HttpGet]
    public async Task<ActionResult<AuditPageDto>> List([FromQuery] AuditQueryDto query) =>
        Ok(await _audit.ListAsync(query));

    // GET /audit/verify — recompute the org's whole hash chain and report the
    // first break, if any. This is the answer to "has anyone edited this log?".
    [HttpGet("verify")]
    public async Task<ActionResult<AuditVerificationDto>> Verify() =>
        Ok(await _audit.VerifyAsync());
}
