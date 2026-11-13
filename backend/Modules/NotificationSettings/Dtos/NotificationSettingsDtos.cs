using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Modules.NotificationSettings.Entities;

namespace AltomateHR.Api.Modules.NotificationSettings.Dtos;

// Settings → Notifications: every notification the company can configure,
// and the admins it can pick from.
public class NotificationSettingsDto
{
    public List<NotificationAdminDto> Admins { get; set; } = new();
    public List<NotificationSettingDto> Notifications { get; set; } = new();
}

// Someone who may be picked: the owner and every admin.
public class NotificationAdminDto
{
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;

    // Their access covers only some staff (Manage access → Employees in
    // scope), so they only hear about those people.
    public bool IsLimited { get; set; }
}

public class NotificationSettingDto
{
    public NotificationKind Kind { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public List<string> AdminUserIds { get; set; } = new();
    public bool EmailStaff { get; set; }

    // Null until someone saves it (the defaults are in use).
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedByName { get; set; }
}

public class UpdateNotificationSettingDto
{
    public bool Enabled { get; set; }

    [MaxLength(100)]
    public List<string> AdminUserIds { get; set; } = new();

    public bool EmailStaff { get; set; }
}
