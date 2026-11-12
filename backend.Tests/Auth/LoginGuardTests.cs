using AltomateHR.Api.Modules.Auth;

namespace AltomateHR.Api.Tests.Auth;

// The lockout rules on their own, with the clock in the test's hands.
public class LoginGuardTests
{
    // 12 Nov 2026, 10:00 in Malaysia.
    private static readonly DateTime T = new(2026, 11, 12, 2, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AnAccount_LocksAtTheFifthWrongPassword_ForFifteenMinutes()
    {
        var guard = new LoginGuard();

        for (var i = 0; i < 4; i++)
            Assert.Null(guard.RecordFailure("a@x.test", null, T.AddMinutes(i)).AccountLockedFor);
        Assert.Equal(TimeSpan.FromMinutes(15), guard.RecordFailure("A@X.test ", null, T.AddMinutes(4)).AccountLockedFor);

        Assert.True(guard.Check("a@x.test", null, T.AddMinutes(5))!.ForAccount);
        Assert.Null(guard.Check("a@x.test", null, T.AddMinutes(19.5)));
    }

    [Fact]
    public void WrongPasswordsSpreadOver_MoreThanTheWindow_NeverLock()
    {
        var guard = new LoginGuard();

        for (var i = 0; i < 10; i++) guard.RecordFailure("a@x.test", null, T.AddMinutes(i * 4));

        Assert.Null(guard.Check("a@x.test", null, T.AddMinutes(40)));
    }

    [Fact]
    public void ASuccess_ClearsTheAccount_ButNotTheIp()
    {
        var guard = new LoginGuard();
        for (var i = 0; i < 4; i++) guard.RecordFailure("a@x.test", "1.1.1.1", T);
        guard.RecordSuccess("a@x.test");
        for (var i = 0; i < 4; i++) guard.RecordFailure("a@x.test", "1.1.1.1", T);
        Assert.Null(guard.Check("a@x.test", null, T));

        // 8 so far from this IP; two more for anyone block it.
        guard.RecordFailure("b@x.test", "1.1.1.1", T);
        var tenth = guard.RecordFailure("c@x.test", "1.1.1.1", T);
        Assert.Equal(TimeSpan.FromMinutes(15), tenth.IpBlockedFor);
        Assert.Equal(["a@x.test", "b@x.test", "c@x.test"], tenth.EmailsTriedFromIp);
        Assert.False(guard.Check("z@x.test", "1.1.1.1", T)!.ForAccount);
        Assert.Null(guard.Check("z@x.test", "2.2.2.2", T));
    }

    [Fact]
    public void ASecondBlockTheSameDay_IsAnHour_AndTheNextDayIsBackToFifteenMinutes()
    {
        var guard = new LoginGuard();

        Assert.Equal(TimeSpan.FromMinutes(15), TenFrom(guard, "1.1.1.1", T).IpBlockedFor);

        var later = T.AddMinutes(30);
        var second = TenFrom(guard, "1.1.1.1", later);
        Assert.Equal(TimeSpan.FromHours(1), second.IpBlockedFor);
        Assert.True(second.IpBlockedAgainToday);

        Assert.Equal(TimeSpan.FromMinutes(15), TenFrom(guard, "1.1.1.1", T.AddDays(1)).IpBlockedFor);
    }

    [Fact]
    public void ResettingThePassword_LiftsTheLock()
    {
        var guard = new LoginGuard();
        for (var i = 0; i < 5; i++) guard.RecordFailure("a@x.test", null, T);

        guard.ClearAccount("a@x.test");

        Assert.Null(guard.Check("a@x.test", null, T));
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
        LoginFailureOutcome last = LoginFailureOutcome.Nothing;
        for (var i = 0; i < 10; i++) last = guard.RecordFailure($"user{i}@x.test", ip, at);
        return last;
    }
}
