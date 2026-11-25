using AltomateHR.Api.Modules.Activity.Dtos;
using AltomateHR.Api.Modules.ApiKeys;
using AltomateHR.Api.Modules.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltomateHR.Api.Modules.Activity;

// Who signed in, and what they did — across every company. Fusioneta
// superadmins only (the Support page's "User activity" tab); never shown to a
// customer, and never to a machine key. Same gate as ApiMonitoringController.
//
// This is the ONLY reader of UserSessions / UserActivityEvents, which are not
// tenant-scoped — keep it behind the superadmin policy.
[ApiController]
[HumanOnly]
[Route("platform/activity")]
[Authorize(Policy = AuthPolicies.Superadmin)]
public class PlatformActivityController : ControllerBase
{
    private readonly IActivityService _activity;

    public PlatformActivityController(IActivityService activity) => _activity = activity;

    // GET /platform/activity/sessions?from&to&organizationId&search&via&onlineOnly&includeStaff&page&limit
    // Sign-ins, newest first.
    [HttpGet("sessions")]
    public async Task<ActionResult<SessionPageDto>> Sessions([FromQuery] SessionQueryDto query) =>
        Ok(await _activity.ListSessionsAsync(query));

    // GET /platform/activity/sessions/{id} — one sign-in and everything done in it.
    [HttpGet("sessions/{id}")]
    public async Task<ActionResult<SessionDetailDto>> Session(string id)
    {
        var detail = await _activity.GetSessionAsync(id);
        return detail is null ? NotFound(new { message = "Session not found." }) : Ok(detail);
    }
}
