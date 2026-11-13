using AltomateHR.Api.Modules.ApiKeys;
using AltomateHR.Api.Modules.NotificationSettings.Dtos;
using AltomateHR.Api.Modules.NotificationSettings.Entities;
using AltomateHR.Api.Modules.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltomateHR.Api.Modules.NotificationSettings;

// Settings → Notifications: who hears about what, per company.
[ApiController]
[Route("notification-settings")]
[Authorize(Roles = "Admin,Owner")]
public class NotificationSettingsController : ControllerBase
{
    private readonly INotificationSettingService _settings;

    public NotificationSettingsController(INotificationSettingService settings) => _settings = settings;

    // GET /notification-settings — every notification and the admins to pick from.
    [RequireScope("organizations:read")]
    [HttpGet]
    public async Task<ActionResult<NotificationSettingsDto>> Get() => Ok(await _settings.GetAsync());

    // PUT /notification-settings/DOCUMENT_EXPIRY — save one notification's choices.
    [RequireScope("organizations:write")]
    [RequireSettings]
    [HttpPut("{kind}")]
    public async Task<ActionResult<NotificationSettingDto>> Update(
        NotificationKind kind, UpdateNotificationSettingDto dto)
    {
        try
        {
            return Ok(await _settings.UpdateAsync(kind, dto));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
