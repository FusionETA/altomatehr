using AltomateHR.Api.Modules.Notifications;
using AltomateHR.Api.Modules.Notifications.Dtos;
using AltomateHR.Api.Modules.Notifications.Entities;

namespace AltomateHR.Api.Tests.Support;

// A notification sink for tests of the services that emit them.
//
// Shared rather than re-declared per test file: claims, leave, attendance and
// overtime all take INotificationService now, and four private copies of the
// same no-op is how they drift.
//
// Records what was sent, so a test that cares can assert on it — but the
// default is to ignore it entirely, which mirrors production: NotifyAsync is
// best-effort and must never fail the approve or submit that triggered it, so
// no behaviour under test should depend on it.
internal sealed class FakeNotificationService : INotificationService
{
    public List<(string OrganizationId, string UserId, NotificationType Type, string Title, string Body, string? Url)> Sent { get; } = [];

    public Task<string?> NotifyAsync(
        string organizationId, string userId, NotificationType type, string title, string body, string? url = null)
    {
        Sent.Add((organizationId, userId, type, title, body, url));
        return Task.FromResult<string?>($"ntf-{Sent.Count}");
    }

    public Task<NotificationListDto> GetForCurrentUserAsync() =>
        Task.FromResult(new NotificationListDto { Notifications = [], UnreadCount = 0 });

    public Task<bool> MarkReadAsync(string id) => Task.FromResult(true);

    public Task MarkAllReadAsync() => Task.CompletedTask;
}
