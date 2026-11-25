using System.ComponentModel.DataAnnotations;

namespace AltomateHR.Api.Modules.Activity.Dtos;

// ---- Ingest: what the browser tracker sends ----

// POST /activity/events. An empty list is a heartbeat: the tab is open and
// visible, so the session is still "online". Everything identifying — user,
// company, session, IP — is read off the token server-side, never from here.
public class ActivityBatchDto
{
    [MaxLength(50)]
    public List<ActivityEventInDto> Events { get; set; } = [];
}

public class ActivityEventInDto
{
    // "page" or "click". Actions are recorded by the server itself, so a
    // browser cannot report one.
    [Required, MaxLength(10)]
    public string Kind { get; set; } = string.Empty;

    // A page id ("payroll/runs") or a data-track id ("payslips.export").
    [Required, MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    // When it happened in the browser. Clamped server-side to the last few
    // minutes, so a wrong clock cannot write history.
    public DateTime? At { get; set; }
}

// ---- Read: the Fusioneta Support "User activity" tab ----

public class SessionQueryDto
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string? OrganizationId { get; set; }

    // Name or email, contains.
    [MaxLength(200)]
    public string? Search { get; set; }

    // "password", "Altomate SSO", "resumed".
    [MaxLength(40)]
    public string? Via { get; set; }

    // Only sessions seen in the last few minutes and not signed out.
    public bool OnlineOnly { get; set; }

    // Fusioneta's own staff are hidden unless asked for.
    public bool IncludeStaff { get; set; }

    // 1-based.
    public int Page { get; set; } = 1;
    public int Limit { get; set; } = 25;
}

public class SessionPageDto
{
    public List<SessionDto> Sessions { get; set; } = [];
    public int Total { get; set; }
}

public class SessionDto
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string OrganizationId { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Via { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string? Device { get; set; }
    public bool IsStaff { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string? EndReason { get; set; }

    // "online" | "inactive" | "signed-out" — worked out from LastSeenAt/EndedAt.
    public string Status { get; set; } = string.Empty;

    public int PageViews { get; set; }
    public int Clicks { get; set; }
    public int Actions { get; set; }
    public int FailedActions { get; set; }
}

public class SessionDetailDto
{
    public SessionDto Session { get; set; } = new();
    public List<TimelineItemDto> Items { get; set; } = [];

    // The timeline is capped; true when there was more than shown.
    public bool Truncated { get; set; }
}

public class TimelineItemDto
{
    public DateTime At { get; set; }

    // "page" | "click" | "action" | "error". An "error" is a failed READ from
    // the API request log — a failed write is already an "action" with a
    // status, and carries the error message on it.
    public string Kind { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string? TargetId { get; set; }
    public int? StatusCode { get; set; }

    // Page: time until the next page (or the session's last sign of life),
    // capped. Action: how long the request took.
    public int? DurationMs { get; set; }

    // What the person was shown when it failed, from the API request log.
    public string? Message { get; set; }

    public bool IsSupport { get; set; }

    // Set only when it differs from the session's own company (they switched).
    public string? CompanyName { get; set; }
}
