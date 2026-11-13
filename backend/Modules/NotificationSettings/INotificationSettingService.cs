using AltomateHR.Api.Modules.NotificationSettings.Dtos;
using AltomateHR.Api.Modules.NotificationSettings.Entities;

namespace AltomateHR.Api.Modules.NotificationSettings;

public interface INotificationSettingService
{
    // Settings → Notifications for the current company.
    Task<NotificationSettingsDto> GetAsync();

    // Saves one notification's choices. Throws ArgumentException when a picked
    // person isn't an admin or the owner here.
    Task<NotificationSettingDto> UpdateAsync(NotificationKind kind, UpdateNotificationSettingDto dto);

    // What a daily job needs for a NAMED company: whether it's on, whether to
    // email staff, and the admins to tell — the ticked ones, or the owner when
    // nobody is ticked (or every ticked admin has since left).
    Task<EffectiveNotificationSetting> ForOrgAsync(string organizationId, NotificationKind kind);
}

// Owners: the company's owner(s), who also get anything none of the chosen
// admins covers (every chosen admin limited to other staff).
// ReminderDays: how many days before a date to remind, earliest first
// (e.g. [60, 30]); the reminder on the day itself is separate.
public sealed record EffectiveNotificationSetting(
    bool Enabled, bool EmailStaff, IReadOnlyList<NotificationRecipient> Admins,
    IReadOnlyList<string> Owners, IReadOnlyList<int> ReminderDays);

// An admin to tell. PolicyScope set = their access covers only the staff on
// those policies (IEmployeeScopeResolver turns it into people).
public sealed record NotificationRecipient(string UserId, IReadOnlyList<string>? PolicyScope);
