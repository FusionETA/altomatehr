namespace AltomateHR.Api.Modules.Partners.Dtos;

// POST /partner/token — body. The client secret rides in the Authorization
// header (Bearer), not here.
public class PartnerTokenRequestDto
{
    public string Ticket { get; set; } = string.Empty;
}

// POST /partner/token/refresh — body.
public class PartnerRefreshRequestDto
{
    public string RefreshToken { get; set; } = string.Empty;
}

// The token-exchange result. `user` + `organization` let the partner find/create
// its own local record (storing only the IDs) without a second round-trip.
public class PartnerTokenResponseDto
{
    public string AccessToken { get; set; } = string.Empty;
    public string? RefreshToken { get; set; }
    public int ExpiresIn { get; set; }                 // seconds
    public PartnerUserDto User { get; set; } = new();
    public PartnerOrgDto Organization { get; set; } = new();
}

public class PartnerUserDto
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;   // the user's role IN this org
}

public class PartnerOrgDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

// POST /partner/notifications — body. Requires the "notifications:write" scope.
// OrganizationId is required (not inferred): ApiClient rows are NOT tenant-scoped —
// one client secret serves every customer org — so there's no "the caller's org"
// to fall back on the way a partner ACCESS TOKEN would carry one.
public class SendPartnerNotificationDto
{
    public string UserId { get; set; } = string.Empty;
    public string OrganizationId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public NotificationChannel Channel { get; set; } = NotificationChannel.Both;

    // Deep link back into the partner app (e.g. AppraisifyAlt's own appraisal
    // page) — opaque to AltomateHR, stored as the notification's Url.
    public string? Link { get; set; }
}

public enum NotificationChannel
{
    Email,
    NotificationCenter,
    Both,
}

public class SendPartnerNotificationResponseDto
{
    public PartnerNotificationStatus Status { get; set; }
    public string? NotificationId { get; set; }
}

// Forbidden is deliberately NOT part of the wire contract's Status values
// (Success/Failed/UserNotFound only) — a missing scope is refused as an
// HTTP 403 before a response body is ever built. It lives in this enum only
// so the service has one return type to hand the controller.
public enum PartnerNotificationStatus
{
    Success,
    Failed,
    UserNotFound,
    Forbidden,
}
