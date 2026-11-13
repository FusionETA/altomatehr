using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Employees;

namespace AltomateHR.Api.Modules.DocumentExpiry.Entities;

// Which reminder went out for which document date. One row per (person,
// document, expiry date, stage), unique, so each reminder goes once; a new
// expiry date has no rows yet, so renewing starts the reminders over.
public class DocumentExpiryReminder : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;

    [MaxLength(40)]
    public string UserId { get; set; } = string.Empty;

    public IdentityDocument Document { get; set; }

    // The expiry date this reminder was about (date only).
    public DateTime ExpiryDate { get; set; }

    public DocumentReminderStage Stage { get; set; }

    public DateTime SentAt { get; set; }
}

public enum DocumentReminderStage
{
    // Up to 30 days before (sent once, the first day it is due).
    BEFORE,

    // On the expiry day.
    ON_THE_DAY,

    // Already past when first seen — a date entered late, or one that had
    // lapsed before reminders were switched on. Sent once.
    EXPIRED,
}
