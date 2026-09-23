using AltomateHR.Api.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltomateHR.Api.Modules.Policies;

// What the SIGNED-IN employee's own policy allows.
//
// Separate from PoliciesController, which is Admin/Owner-only because editing
// policies is an admin concern. Reading which modules your own policy includes
// is not: the employee shell needs it to decide what to show, and every
// employee is entitled to know what they have access to.
[ApiController]
[Route("policies/mine")]
[Authorize]
public class MyPolicyController : ControllerBase
{
    private readonly IPolicyService _policies;
    private readonly ICurrentUser _currentUser;

    public MyPolicyController(IPolicyService policies, ICurrentUser currentUser)
    {
        _policies = policies;
        _currentUser = currentUser;
    }

    // GET /policies/mine/modules
    //
    // Advisory only — the gate on each endpoint is the real enforcement. This
    // exists so nobody is offered a screen that will refuse them.
    //
    // An administrative seat gets everything: their access comes from their
    // role, and an org whose default policy happens to exclude a module should
    // not lose the screens that configure it.
    [HttpGet("modules")]
    public async Task<IActionResult> Modules()
    {
        var access = _currentUser.UserId is { Length: > 0 } userId
                     && !OrgRoles.IsAdministrative(_currentUser.Role)
            ? await _policies.GetModuleAccessAsync(userId)
            : PolicyModuleAccess.All;

        return Ok(new
        {
            attendance = access.Attendance,
            claims = access.Claims,
            leave = access.Leave,
        });
    }
}
