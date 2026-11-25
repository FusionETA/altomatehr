namespace AltomateHR.Api.Modules.Activity;

// "Chrome · Windows" from a user-agent string. Deliberately coarse: enough to
// tell a phone from a laptop when someone says "it broke on my phone", without
// storing a fingerprint-grade raw user agent.
public static class DeviceName
{
    public static string? From(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return null;
        var ua = userAgent;

        // Order matters: Edge and Opera also say "Chrome"; Chrome also says "Safari".
        var browser =
            Has(ua, "Edg/") ? "Edge"
            : Has(ua, "OPR/") || Has(ua, "Opera") ? "Opera"
            : Has(ua, "SamsungBrowser") ? "Samsung Internet"
            : Has(ua, "Firefox/") || Has(ua, "FxiOS") ? "Firefox"
            : Has(ua, "Chrome/") || Has(ua, "CriOS") ? "Chrome"
            : Has(ua, "Safari/") ? "Safari"
            : null;

        var os =
            Has(ua, "iPhone") || Has(ua, "iPad") || Has(ua, "iPod") ? "iOS"
            : Has(ua, "Android") ? "Android"
            : Has(ua, "Windows") ? "Windows"
            : Has(ua, "Mac OS X") || Has(ua, "Macintosh") ? "macOS"
            : Has(ua, "CrOS") ? "ChromeOS"
            : Has(ua, "Linux") ? "Linux"
            : null;

        if (browser is null && os is null) return "Other";
        return string.Join(" · ", new[] { browser, os }.Where(p => p is not null));
    }

    private static bool Has(string ua, string token) => ua.Contains(token, StringComparison.OrdinalIgnoreCase);
}
