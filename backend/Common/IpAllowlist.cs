namespace AltomateHR.Api.Common;

// IPv4 allowlist matching, ported from the monolith's lib/ip-whitelist.ts.
//
// The previous implementation here compared strings for equality, which meant
// a CIDR range never matched anything: an allowlist of "203.106.51.0/24"
// rejected 203.106.51.7 and every other address on that network. A range is
// the normal way to express "the office", so in practice the feature only
// worked for allowlists of individual addresses.
//
// IPv4 only — the legacy note still applies (Malaysian office networks are
// overwhelmingly IPv4), and an IPv6 client simply won't match, which the
// caller treats as off-network rather than as an error.
public static class IpAllowlist
{
    // A parsed entry, reduced to its network address and prefix length so a
    // match is two integer operations.
    public readonly record struct Entry(uint Network, int Prefix);

    // Unparseable entries are DROPPED, not treated as errors. One malformed
    // row an admin typed must not invalidate the rest of the list and lock a
    // whole site out of clocking in.
    public static List<Entry> Parse(IEnumerable<string?> entries)
    {
        var parsed = new List<Entry>();
        foreach (var raw in entries)
        {
            if (TryParseEntry(raw, out var entry)) parsed.Add(entry);
        }
        return parsed;
    }

    /// Splits the legacy comma-separated form before parsing.
    public static List<Entry> ParseCsv(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? []
            : Parse(csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    public static bool Matches(string? ip, IReadOnlyList<Entry> allowlist)
    {
        if (allowlist.Count == 0) return false;
        if (!TryParseIpv4(ip, out var address)) return false;

        foreach (var entry in allowlist)
        {
            if (entry.Prefix == 0) return true;   // 0.0.0.0/0 — everything
            var mask = MaskFor(entry.Prefix);
            if ((address & mask) == entry.Network) return true;
        }
        return false;
    }

    /// For validating what an admin typed before it is saved, rather than
    /// letting it be silently dropped at match time.
    public static bool IsValidEntry(string? entry) => TryParseEntry(entry, out _);

    // A bare address is a /32. The address is masked down to its network so
    // "203.106.51.42/24" and "203.106.51.0/24" behave identically — admins
    // routinely type the former.
    private static bool TryParseEntry(string? raw, out Entry entry)
    {
        entry = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var parts = raw.Trim().Split('/');
        if (parts.Length > 2) return false;
        if (!TryParseIpv4(parts[0], out var address)) return false;

        var prefix = 32;
        if (parts.Length == 2)
        {
            if (!int.TryParse(parts[1], out prefix) || prefix < 0 || prefix > 32) return false;
        }

        entry = new Entry(address & MaskFor(prefix), prefix);
        return true;
    }

    private static uint MaskFor(int prefix) =>
        prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);

    // Deliberately stricter than IPAddress.TryParse, which accepts "10.1",
    // octal-looking padded octets and other historical forms that would make
    // an allowlist mean something other than what the admin read.
    private static bool TryParseIpv4(string? ip, out uint value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(ip)) return false;

        var octets = ip.Trim().Split('.');
        if (octets.Length != 4) return false;

        foreach (var octet in octets)
        {
            if (octet.Length is 0 or > 3) return false;
            if (!byte.TryParse(octet, out var n)) return false;
            // Rejects "01" / "001": byte.TryParse accepts them, but a padded
            // octet is ambiguous enough that it should not silently widen or
            // narrow what an allowlist covers.
            if (n.ToString() != octet) return false;
            value = (value << 8) | n;
        }
        return true;
    }
}
