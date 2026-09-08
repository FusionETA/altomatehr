using System.Text.Json;
using WebPush;

namespace AltomateHR.Api.Modules.Notifications;

public class WebPushService : IWebPushService
{
    // Hand-serialized (outside MVC, like RealtimeEventDto), so it needs its own
    // camelCase options to match what a service worker's `event.data.json()`
    // expects — the same convention Program.cs sets up for the REST endpoints.
    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IWebPushSubscriptionRepository _subscriptions;
    private readonly WebPushClient _client;
    private readonly VapidDetails? _vapid;
    private readonly ILogger<WebPushService> _logger;

    public WebPushService(
        IWebPushSubscriptionRepository subscriptions, IConfiguration config, ILogger<WebPushService> logger)
    {
        _subscriptions = subscriptions;
        _logger = logger;
        _client = new WebPushClient();

        PublicKey = config["Vapid:PublicKey"] ?? string.Empty;
        var privateKey = config["Vapid:PrivateKey"];
        var subject = config["Vapid:Subject"] ?? "mailto:admin@altomatehr.app";

        // Null when unconfigured (e.g. a fresh dev checkout with no VAPID keys
        // generated yet) — SendToUserAsync treats that as "push disabled",
        // never as an error, the same way LoggingEmailSender stands in for a
        // missing EngineMailer key.
        _vapid = !string.IsNullOrWhiteSpace(PublicKey) && !string.IsNullOrWhiteSpace(privateKey)
            ? new VapidDetails(subject, PublicKey, privateKey)
            : null;
    }

    public string PublicKey { get; }

    public Task SubscribeAsync(string userId, string endpoint, string p256dh, string auth) =>
        _subscriptions.UpsertAsync(userId, endpoint, p256dh, auth);

    public Task UnsubscribeAsync(string userId, string endpoint) =>
        _subscriptions.DeleteForUserAsync(userId, endpoint);

    public async Task SendToUserAsync(string userId, string title, string body, string? url = null)
    {
        if (_vapid is null) return;   // VAPID not configured — push is a no-op, not a failure

        List<Entities.WebPushSubscription> devices;
        try
        {
            devices = await _subscriptions.GetForUserAsync(userId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Push subscription lookup failed for {UserId}", userId);
            return;
        }

        if (devices.Count == 0) return;

        var payload = JsonSerializer.Serialize(new { title, body, url }, PayloadOptions);

        foreach (var device in devices)
        {
            try
            {
                var subscription = new PushSubscription(device.Endpoint, device.P256dh, device.Auth);
                await _client.SendNotificationAsync(subscription, payload, _vapid);
            }
            catch (WebPushException ex)
            {
                // 410 Gone / 404 Not Found — the browser revoked this endpoint
                // (uninstalled, cleared site data, etc.). Clean it up so future
                // sends don't keep paying for a dead device.
                if (ex.StatusCode is System.Net.HttpStatusCode.Gone or System.Net.HttpStatusCode.NotFound)
                {
                    try { await _subscriptions.DeleteAsync(device.Id); }
                    catch { /* best-effort cleanup */ }
                }
                else
                {
                    _logger.LogWarning(ex, "Push send failed for {UserId} ({StatusCode})", userId, ex.StatusCode);
                }
            }
            catch (Exception ex)
            {
                // Never let a push failure break the flow that triggered it —
                // same contract as INotificationService.NotifyAsync.
                _logger.LogWarning(ex, "Push send failed for {UserId}", userId);
            }
        }
    }
}
