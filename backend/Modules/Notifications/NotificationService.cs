using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Notifications.Dtos;
using AltomateHR.Api.Modules.Notifications.Entities;

namespace AltomateHR.Api.Modules.Notifications;

public class NotificationService : INotificationService
{
    private readonly INotificationRepository _repo;
    private readonly IWebPushService _push;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        INotificationRepository repo, IWebPushService push, ICurrentUser currentUser, ILogger<NotificationService> logger)
    {
        _repo = repo;
        _push = push;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<string?> NotifyAsync(
        string organizationId, string userId, NotificationType type, string title, string body, string? url = null)
    {
        if (string.IsNullOrEmpty(organizationId) || string.IsNullOrEmpty(userId)) return null;

        string? notificationId = null;
        try
        {
            var notification = new Notification
            {
                OrganizationId = organizationId,
                UserId = userId,
                Type = type,
                Title = title,
                Body = body,
                Url = url,
                CreatedAt = DateTime.UtcNow,
            };
            await _repo.AddAsync(notification);
            notificationId = notification.Id;
        }
        catch (Exception ex)
        {
            // The caller has already committed a claim / leave / attendance
            // decision. Losing the notification costs the user a missed bell
            // entry; rethrowing here would cost them the decision.
            _logger.LogWarning(ex, "Notification persist failed for {UserId}/{Type}", userId, type);
        }

        // Best-effort on top of the persisted row, same as the reference app's
        // notify(): a user with no registered device (or push unconfigured)
        // just gets the bell entry, silently.
        await _push.SendToUserAsync(userId, title, body, url);

        return notificationId;
    }

    public async Task<NotificationListDto> GetForCurrentUserAsync()
    {
        var userId = _currentUser.UserId;
        if (string.IsNullOrEmpty(userId)) return new NotificationListDto();

        var notifications = await _repo.ListForUserAsync(userId);
        var unreadCount = await _repo.UnreadCountAsync(userId);

        return new NotificationListDto
        {
            Notifications = notifications.Select(ToDto).ToList(),
            UnreadCount = unreadCount,
        };
    }

    public Task<bool> MarkReadAsync(string id)
    {
        var userId = _currentUser.UserId;
        return string.IsNullOrEmpty(userId) ? Task.FromResult(false) : _repo.MarkReadAsync(userId, id);
    }

    public Task MarkAllReadAsync()
    {
        var userId = _currentUser.UserId;
        return string.IsNullOrEmpty(userId) ? Task.CompletedTask : _repo.MarkAllReadAsync(userId);
    }

    private static NotificationDto ToDto(Notification n) => new()
    {
        Id = n.Id,
        Type = n.Type,
        Title = n.Title,
        Body = n.Body,
        Url = n.Url,
        Read = n.ReadAt != null,
        CreatedAt = n.CreatedAt,
    };
}
