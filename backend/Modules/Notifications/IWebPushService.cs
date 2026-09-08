namespace AltomateHR.Api.Modules.Notifications;

// Browser push (the Push API + a service worker), separate from the persisted
// bell (INotificationService) — NotificationService.NotifyAsync calls this
// internally so every existing caller gets push "for free", the same way the
// reference app's notify() bundles a DB write with a push send.
public interface IWebPushService
{
    // The VAPID public key the client passes into
    // PushManager.subscribe({ applicationServerKey }). Empty when unconfigured.
    string PublicKey { get; }

    Task SubscribeAsync(string userId, string endpoint, string p256dh, string auth);
    Task UnsubscribeAsync(string userId, string endpoint);

    // Pushes to every device registered for the user. Best-effort and NEVER
    // throws, same contract as INotificationService.NotifyAsync — a push is a
    // nicety on top of the persisted notification, not a dependency of it.
    Task SendToUserAsync(string userId, string title, string body, string? url = null);
}
