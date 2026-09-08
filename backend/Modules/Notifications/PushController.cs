using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Notifications.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltomateHR.Api.Modules.Notifications;

[ApiController]
[Route("push")]
[Authorize]
public class PushController : ControllerBase
{
    private readonly IWebPushService _push;
    private readonly ICurrentUser _currentUser;

    public PushController(IWebPushService push, ICurrentUser currentUser)
    {
        _push = push;
        _currentUser = currentUser;
    }

    // GET /push/public-key — the client passes this straight into
    // PushManager.subscribe({ applicationServerKey: ... }).
    [HttpGet("public-key")]
    public ActionResult<VapidPublicKeyDto> GetPublicKey() =>
        Ok(new VapidPublicKeyDto { PublicKey = _push.PublicKey });

    // POST /push/subscribe — registers (or refreshes) the caller's device.
    [HttpPost("subscribe")]
    public async Task<IActionResult> Subscribe(SavePushSubscriptionDto dto)
    {
        var userId = _currentUser.UserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        await _push.SubscribeAsync(userId, dto.Endpoint, dto.Keys.P256dh, dto.Keys.Auth);
        return Ok(new { ok = true });
    }

    // POST /push/unsubscribe — removes one device, scoped to the caller.
    [HttpPost("unsubscribe")]
    public async Task<IActionResult> Unsubscribe(UnsubscribePushDto dto)
    {
        var userId = _currentUser.UserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        await _push.UnsubscribeAsync(userId, dto.Endpoint);
        return Ok(new { ok = true });
    }
}
