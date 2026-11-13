namespace AltomateHR.Api.Modules.NotificationSettings.Entities;

// A notification a company can configure in Settings → Notifications. Each
// one has its own row (NotificationSetting): on/off, which admins get it,
// and whether the staff member it is about is emailed too. Add a value here
// (plus its copy in NotificationSettingService.Describe) for the next one.
public enum NotificationKind
{
    // Work permit and passport expiry reminders (Modules/DocumentExpiry).
    DOCUMENT_EXPIRY,
}
