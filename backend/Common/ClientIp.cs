namespace AltomateHR.Api.Common;

// The caller's real IP, the one place it is worked out.
//
// We sit behind ONE reverse proxy (nginx on the droplet), which appends the
// real client to X-Forwarded-For (`$proxy_add_x_forwarded_for`). So the LAST
// hop is the IP nginx actually saw and is trustworthy; the FIRST hops are
// whatever the client sent and are spoofable — a client could set
// `X-Forwarded-For: <an-allowed-ip>` to slip past the attendance allowlist, or
// a new value on every request to dodge the login limits. A security control
// must therefore never trust the first hop.
//
// The connection's own address is nginx's, the same for everyone — keying a
// rate limit on it made one bucket for the whole app.
//
// (Assumes exactly one trusted proxy in front. If that ever changes, move to
// ForwardedHeaders middleware with KnownProxies instead.)
public static class ClientIp
{
    public static string? Of(HttpContext? context)
    {
        if (context is null) return null;

        var forwarded = context.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            var hops = forwarded.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (hops.Length > 0) return hops[^1];   // last hop = the proxy-appended real client
        }

        return context.Connection.RemoteIpAddress?.ToString();
    }
}
