using AltomateHR.Api.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltomateHR.Api.Modules.Partners;

// SSO launch — the generalised, registry-driven version of a per-app
// "/sso/appraisify" redirect. One route serves every app: the {app} slug looks up
// the registered redirectUrl, so adding an app is a registry row, not a new route.
[ApiController]
[Route("sso")]
[Authorize]
public class SsoController : ControllerBase
{
    private readonly IPartnerAuthService _partners;
    private readonly ICurrentUser _currentUser;

    public SsoController(IPartnerAuthService partners, ICurrentUser currentUser)
    {
        _partners = partners;
        _currentUser = currentUser;
    }

    // GET /sso/launch/{app} — a signed-in user clicks e.g. "Appraisify". Rides
    // on the JWT session, mints a single-use ticket for the caller's active org,
    // and 302-redirects to the app's callback with ?t=<ticket>. Only the ticket id
    // is on the wire — meaningless without the Redis entry.
    [HttpGet("launch/{app}")]
    public async Task<IActionResult> Launch(string app)
    {
        var userId = _currentUser.UserId;
        var orgId = _currentUser.OrganizationId;
        if (userId is null || orgId is null) return Unauthorized();

        var redirectUrl = await _partners.MintLaunchTicketAsync(app, userId, orgId);
        return redirectUrl is null
            ? NotFound(new { error = new { status = 404, message = $"Unknown or inactive app: {app}." } })
            : Redirect(redirectUrl);
    }
}
