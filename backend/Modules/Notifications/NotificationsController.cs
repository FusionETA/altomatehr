using AltomateHR.Api.Modules.Notifications.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltomateHR.Api.Modules.Notifications;

[ApiController]
[Route("notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _notifications;

    public NotificationsController(INotificationService notifications) => _notifications = notifications;

    // GET /notifications — the current user's own notifications + unread
    // count, for the header bell. Any authenticated role.
    [HttpGet]
    public async Task<ActionResult<NotificationListDto>> Get() =>
        Ok(await _notifications.GetForCurrentUserAsync());

    // POST /notifications/read — { id } marks one read, { all: true } marks
    // every unread one read. Scoped to the caller, so a forged id can't
    // acknowledge another user's notification.
    [HttpPost("read")]
    public async Task<IActionResult> MarkRead(MarkNotificationsReadDto dto)
    {
        if (dto.All)
        {
            await _notifications.MarkAllReadAsync();
            return Ok(new { ok = true });
        }

        if (string.IsNullOrWhiteSpace(dto.Id))
            return BadRequest(new { message = "Provide either { id } or { all: true }." });

        return await _notifications.MarkReadAsync(dto.Id) ? Ok(new { ok = true }) : NotFound();
    }
}
