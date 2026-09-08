using AltomateHR.Api.Modules.Notifications.Entities;

namespace AltomateHR.Api.Modules.Notifications.Dtos;

public class NotificationDto
{
    public string Id { get; set; } = string.Empty;
    public NotificationType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? Url { get; set; }
    public bool Read { get; set; }
    public DateTime CreatedAt { get; set; }
}

// GET /notifications response: the bell needs both the list and the unread
// count in one round trip.
public class NotificationListDto
{
    public List<NotificationDto> Notifications { get; set; } = new();
    public int UnreadCount { get; set; }
}
