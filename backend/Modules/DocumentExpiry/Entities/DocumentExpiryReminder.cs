using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Employees;

namespace AltomateHR.Api.Modules.DocumentExpiry.Entities;

// Which reminder went out for which document date. One row per (person,
// document, expiry date, stage, days left), unique, so each reminder goes
// once; a new expiry date has no rows yet, so renewing starts them over.
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

    // Days to the expiry date when it was sent (0 on the day, negative once
    // expired). Tells a 60-days-before reminder from a 30-days-before one.
    public int DaysLeft { get; set; }

    public DateTime SentAt { get; set; }
}

public enum DocumentReminderStage
{
    // Ahead of the date, at the company's chosen days before (Settings →
    // Notifications). Once per chosen point, on the first day it is due.
    BEFORE,

    // On the expiry day.
    ON_THE_DAY,

    // Already past when first seen — a date entered late, or one that had
    // lapsed before reminders were switched on. Sent once.
    EXPIRED,
}
