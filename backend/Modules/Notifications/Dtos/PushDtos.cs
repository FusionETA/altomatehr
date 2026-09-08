using System.ComponentModel.DataAnnotations;

namespace AltomateHR.Api.Modules.Notifications.Dtos;

// GET /push/public-key response — the client passes this straight into
// PushManager.subscribe({ applicationServerKey: ... }).
public class VapidPublicKeyDto
{
    public string PublicKey { get; set; } = string.Empty;
}

// POST /push/subscribe body. Shaped to match the browser's own
// PushSubscription.toJSON() output exactly (endpoint + keys.p256dh/auth), so
// the client can send that object straight through with no reshaping.
public class SavePushSubscriptionDto
{
    [Required, MaxLength(500)]
    public string Endpoint { get; set; } = string.Empty;

    [Required]
    public PushSubscriptionKeysDto Keys { get; set; } = new();
}

public class PushSubscriptionKeysDto
{
    [Required, MaxLength(255)]
    public string P256dh { get; set; } = string.Empty;

    [Required, MaxLength(255)]
    public string Auth { get; set; } = string.Empty;
}

// POST /push/unsubscribe body.
public class UnsubscribePushDto
{
    [Required, MaxLength(500)]
    public string Endpoint { get; set; } = string.Empty;
}
