using AltomateHR.Api.Modules.NotificationSettings.Entities;

namespace AltomateHR.Api.Modules.NotificationSettings;

public interface INotificationSettingRepository
{
    // The current company's saved rows (tenant filter).
    Task<List<NotificationSetting>> GetAllForCurrentOrgAsync();

    // One company's row, named explicitly — for the daily jobs, which run
    // with no signed-in company.
    Task<NotificationSetting?> GetForOrgAsync(string organizationId, NotificationKind kind);

    Task SaveAsync(NotificationSetting setting);
}
