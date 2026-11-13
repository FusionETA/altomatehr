using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.NotificationSettings.Entities;

// One company's choices for one notification. No row = the defaults
// (NotificationSettingService.Defaults): on, sent to the owner, staff not
// emailed — so a company that never opens the page still hears about it.
public class NotificationSetting : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;

    public NotificationKind Kind { get; set; }

    public bool Enabled { get; set; } = true;

    // The admins (user ids, comma-separated) who get it. Empty = the owner,
    // so a reminder is never sent to nobody.
    [MaxLength(4000)]
    public string? AdminUserIds { get; set; }

    // Also email the staff member it is about. They always get it in the
    // app and as a phone push; this only adds the email.
    public bool EmailStaff { get; set; }

    // For reminders ahead of a date (document expiry): how many days before
    // the first one goes, and optionally a second, closer one. On top of the
    // reminder on the day itself. One choice for the whole company.
    public int FirstReminderDays { get; set; } = 30;
    public int? SecondReminderDays { get; set; }

    public DateTime UpdatedAt { get; set; }

    [MaxLength(40)]
    public string? UpdatedByUserId { get; set; }
}
