using AltomateHR.Api.Modules.Auth;

namespace AltomateHR.Api.Tests.Auth;

// The lockout rules on their own, with the clock in the test's hands.
public class LoginGuardTests
{
    // 12 Nov 2026, 10:00 in Malaysia.
    private static readonly DateTime T = new(2026, 11, 12, 2, 0, 0, DateTimeKind.Utc);

    private static LoginFailureOutcome Wrong(LoginGuard guard, string email, string? ip, DateTime at)
    {
        var attempt = guard.Begin(email, ip, at);
        Assert.Null(attempt.Block);
        return guard.Fail(attempt, at);
    }

    [Fact]
    public void AnAccount_LocksAtTheFifthWrongPassword_ForFifteenMinutes()
    {
        var guard = new LoginGuard();

        for (var i = 0; i < 4; i++)
            Assert.Null(Wrong(guard, "a@x.test", null, T.AddMinutes(i)).AccountLockedFor);
        Assert.Equal(TimeSpan.FromMinutes(15), Wrong(guard, "A@X.test ", null, T.AddMinutes(4)).AccountLockedFor);

        Assert.True(guard.Begin("a@x.test", null, T.AddMinutes(5)).Block!.ForAccount);
        Assert.Null(guard.Begin("a@x.test", null, T.AddMinutes(19.5)).Block);
    }

    [Fact]
    public void WrongPasswordsSpreadOver_MoreThanTheWindow_NeverLock()
    {
        var guard = new LoginGuard();

        for (var i = 0; i < 10; i++) Wrong(guard, "a@x.test", null, T.AddMinutes(i * 4));

        Assert.Null(guard.Begin("a@x.test", null, T.AddMinutes(40)).Block);
    }

    // Counted from the moment an attempt starts: five in flight at once hold
    // the sixth back, whatever they turn out to be.
    [Fact]
    public void AttemptsInFlight_Count_SoABurstCantOutrunTheLock()
    {
        var guard = new LoginGuard();

        var inFlight = Enumerable.Range(0, 5).Select(i => guard.Begin("a@x.test", $"10.0.0.{i}", T)).ToList();
        Assert.All(inFlight, a => Assert.Null(a.Block));
        Assert.True(guard.Begin("a@x.test", "10.0.0.9", T).Block!.ForAccount);

        // The right password takes its attempt back and ends the run.
        guard.Succeed(inFlight[0]);
        Assert.Null(guard.Begin("a@x.test", "10.0.0.9", T).Block);
    }

    [Fact]
    public void ASuccess_ClearsTheAccount_ButNotTheIp()
    {
        var guard = new LoginGuard();
        for (var i = 0; i < 4; i++) Wrong(guard, "a@x.test", "1.1.1.1", T);
        guard.Succeed(guard.Begin("a@x.test", "1.1.1.1", T));
        for (var i = 0; i < 4; i++) Wrong(guard, "a@x.test", "1.1.1.1", T);
        Assert.Null(guard.Begin("a@x.test", null, T).Block);

        // 8 wrong so far from this IP; two more for anyone block it.
        Wrong(guard, "b@x.test", "1.1.1.1", T);
        var tenth = Wrong(guard, "c@x.test", "1.1.1.1", T);
        Assert.Equal(TimeSpan.FromMinutes(15), tenth.IpBlockedFor);
        Assert.Equal(["a@x.test", "b@x.test", "c@x.test"], tenth.EmailsTriedFromIp);
        Assert.False(guard.Begin("z@x.test", "1.1.1.1", T).Block!.ForAccount);
        Assert.Null(guard.Begin("z@x.test", "2.2.2.2", T).Block);
    }

    [Fact]
    public void ASecondBlockTheSameDay_IsAnHour_AndTheNextDayIsBackToFifteenMinutes()
    {
        var guard = new LoginGuard();

        Assert.Equal(TimeSpan.FromMinutes(15), TenFrom(guard, "1.1.1.1", T).IpBlockedFor);

        var second = TenFrom(guard, "1.1.1.1", T.AddMinutes(30));
        Assert.Equal(TimeSpan.FromHours(1), second.IpBlockedFor);
        Assert.True(second.IpBlockedAgainToday);

        Assert.Equal(TimeSpan.FromMinutes(15), TenFrom(guard, "1.1.1.1", T.AddDays(1)).IpBlockedFor);
    }

    // The database finds "ádmin@" and "ａdmin@" as admin@, so they share its lock.
    [Fact]
    public void AccentAndWidthVariants_OfAnEmail_ShareOneLock()
    {
        var guard = new LoginGuard();
        string[] variants = ["admin@x.test", "ádmin@x.test", "ａdmin@x.test", "ADMÍN@x.test", "admin@x.test "];

        foreach (var v in variants) Wrong(guard, v, null, T);

        Assert.True(guard.Begin("admin@x.test", null, T).Block!.ForAccount);
    }

    // One IPv6 client holds a whole /64, so it is counted as one.
    [Fact]
    public void AnIpv6Client_IsCountedByItsSlash64()
    {
        var guard = new LoginGuard();

        for (var i = 0; i < 10; i++) Wrong(guard, $"u{i}@x.test", $"2001:db8:1:2::{i + 1:x}", T);

        Assert.False(guard.Begin("z@x.test", "2001:db8:1:2:ffff::1", T).Block!.ForAccount);
        Assert.Null(guard.Begin("z@x.test", "2001:db8:1:3::1", T).Block);
        Assert.Equal("1.2.3.4", LoginGuard.IpKey("::ffff:1.2.3.4"));
    }

    [Fact]
    public void ResettingThePassword_LiftsTheLock()
    {
        var guard = new LoginGuard();
        for (var i = 0; i < 5; i++) Wrong(guard, "a@x.test", null, T);

        guard.ClearAccount("a@x.test");

        Assert.Null(guard.Begin("a@x.test", null, T).Block);
    }

    [Fact]
    public void TheMessage_SaysHowLongToWait()
    {
        var account = new LoginBlockedException(new LoginBlock(true, T.AddMinutes(14).AddSeconds(5)), T);
        Assert.Equal("Too many wrong passwords for this account. Try again in 15 minutes, or reset your password.", account.Message);

        var ip = new LoginBlockedException(new LoginBlock(false, T.AddSeconds(20)), T);
        Assert.Equal("Too many wrong passwords from your network. Try again in 1 minute.", ip.Message);
    }

    private static LoginFailureOutcome TenFrom(LoginGuard guard, string ip, DateTime at)
    {
        var last = LoginFailureOutcome.Nothing;
        for (var i = 0; i < 10; i++) last = Wrong(guard, $"user{i}@x.test", ip, at);
        return last;
    }
}
