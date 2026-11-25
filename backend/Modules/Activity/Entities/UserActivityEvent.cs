using System.ComponentModel.DataAnnotations;

namespace AltomateHR.Api.Modules.Activity.Entities;

// One thing a signed-in person did, inside a UserSession.
//
// Not tenant-scoped, for the same reason as UserSession (superadmin-only,
// cross-company, saved by a background worker). OrganizationId is copied from
// the token's "org" claim when the event is recorded.
//
// What is NEVER stored: request or response bodies, anything typed, query
// strings, the text on screen. A Name is a page id, a data-track id or a route
// template — vocabulary, not content.
public class UserActivityEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string SessionId { get; set; } = string.Empty;

    [MaxLength(40)]
    public string UserId { get; set; } = string.Empty;

    // The company the person was in at the time — may differ from the
    // session's after a company switch.
    [MaxLength(40)]
    public string? OrganizationId { get; set; }

    public ActivityKind Kind { get; set; }

    // Page:   "payroll/runs"                      (the shell's view id)
    // Click:  "payslips.export"                   (the button's data-track)
    // Action: "POST claims/{id}/approve"          (method + route template)
    [MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    // Action only: the record it touched (the route's {id}), and how it went.
    [MaxLength(40)]
    public string? TargetId { get; set; }

    public int? StatusCode { get; set; }

    public int? DurationMs { get; set; }

    // Done by Fusioneta staff in support mode inside a customer's company.
    public bool IsSupport { get; set; }

    public DateTime CreatedAt { get; set; }
}

public enum ActivityKind
{
    Page,
    Click,
    Action,
}
