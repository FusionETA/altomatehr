using AltomateHR.Api.Common;

namespace AltomateHR.Api.Tests.Attendance;

// The allowlist decides whether someone can clock in at all, so the cases that
// matter are the ones where a wrong answer is silent: a range that matches
// nothing, or a malformed entry that takes the whole list down with it.
public class IpAllowlistTests
{
    [Theory]
    // The regression this replaces: exact string comparison meant a CIDR range
    // matched only the literal text "203.106.51.0/24" and no address at all.
    [InlineData("203.106.51.7", "203.106.51.0/24", true)]
    [InlineData("203.106.51.0", "203.106.51.0/24", true)]
    [InlineData("203.106.51.255", "203.106.51.0/24", true)]
    [InlineData("203.106.52.1", "203.106.51.0/24", false)]
    // A bare address is a /32.
    [InlineData("10.0.0.5", "10.0.0.5", true)]
    [InlineData("10.0.0.6", "10.0.0.5", false)]
    // Admins routinely type a host address with a network prefix; it is masked
    // down, so this covers the same range as 203.106.51.0/24.
    [InlineData("203.106.51.9", "203.106.51.42/24", true)]
    [InlineData("10.1.2.3", "10.0.0.0/8", true)]
    [InlineData("11.1.2.3", "10.0.0.0/8", false)]
    [InlineData("1.2.3.4", "0.0.0.0/0", true)]
    public void Matches_single_entry(string ip, string entry, bool expected) =>
        Assert.Equal(expected, IpAllowlist.Matches(ip, IpAllowlist.ParseCsv(entry)));

    [Fact]
    public void A_malformed_entry_does_not_invalidate_the_rest()
    {
        // "not-an-ip" and the out-of-range prefix are dropped; the good entry
        // still works. The alternative — treating the list as unusable — would
        // lock out a whole site over one typo.
        var list = IpAllowlist.ParseCsv("not-an-ip, 10.0.0.0/33, 192.168.1.0/24");
        Assert.Single(list);
        Assert.True(IpAllowlist.Matches("192.168.1.50", list));
    }

    [Fact]
    public void An_empty_allowlist_matches_nothing()
    {
        // Matches() says "no". Whether that blocks a clock-in is the caller's
        // decision, and AttendanceService treats "no entries" as skip.
        Assert.False(IpAllowlist.Matches("10.0.0.1", IpAllowlist.ParseCsv("")));
        Assert.False(IpAllowlist.Matches("10.0.0.1", IpAllowlist.ParseCsv(null)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("999.1.1.1")]
    [InlineData("10.0.0")]
    [InlineData("::1")]
    public void Unparseable_addresses_never_match(string? ip) =>
        Assert.False(IpAllowlist.Matches(ip, IpAllowlist.ParseCsv("0.0.0.0/0")));

    [Theory]
    // A padded octet is ambiguous enough to reject outright: read as decimal
    // "010" is 10, read as octal it is 8, and an allowlist should not depend
    // on which the reader assumes.
    [InlineData("10.0.0.01", false)]
    [InlineData("010.0.0.1", false)]
    [InlineData("10.0.0.1", true)]
    [InlineData("10.0.0.0/8", true)]
    [InlineData("10.0.0.0/8/9", false)]
    [InlineData("10.0.0.0/-1", false)]
    public void Validates_what_an_admin_typed(string entry, bool valid) =>
        Assert.Equal(valid, IpAllowlist.IsValidEntry(entry));

    [Fact]
    public void Any_matching_entry_is_enough()
    {
        var list = IpAllowlist.ParseCsv("192.168.1.0/24, 203.106.51.0/24, 10.0.0.1");
        Assert.True(IpAllowlist.Matches("203.106.51.7", list));
        Assert.True(IpAllowlist.Matches("10.0.0.1", list));
        Assert.False(IpAllowlist.Matches("172.16.0.1", list));
    }
}
