using System.Collections.Concurrent;

namespace AltomateHR.Api.Modules.Auth;

// Why a sign-in was refused before the password was even checked.
public sealed record LoginBlock(bool ForAccount, DateTime UntilUtc);

// What one wrong password led to: a lock, a block, both or neither.
public sealed record LoginFailureOutcome(
    TimeSpan? AccountLockedFor,
    TimeSpan? IpBlockedFor,
    bool IpBlockedAgainToday,
    IReadOnlyList<string> EmailsTriedFromIp)
{
    public static readonly LoginFailureOutcome Nothing = new(null, null, false, []);
}

// Stops password guessing, in two ways that cover each other:
//
//   • An ACCOUNT is locked for 15 minutes after 5 wrong passwords in a row
//     within 15 minutes. A successful sign-in starts the count again, so a
//     typo then the right password costs nothing. A password reset unlocks it.
//     Kept short so an attacker can't keep a real person locked out for long.
//     Counted for any email, known or not — locking only real accounts would
//     tell a guesser which emails exist.
//
//   • An IP is blocked from signing in for 15 minutes after 10 wrong passwords
//     within 15 minutes, whoever they were for; 1 hour if it was already
//     blocked earlier the same (Malaysian) day. A success does NOT reset it,
//     or a guesser could sign in to their own account between tries. Short,
//     because outlet staff share one wifi IP and phones share carrier IPs.
//
// In memory: one server, and a restart only forgets counts and blocks, which
// errs towards letting people in.
public interface ILoginGuard
{
    // The block in force for this sign-in, or null. `ip` null skips the IP
    // rule (a companion app's server checking a password for someone).
    LoginBlock? Check(string email, string? ip, DateTime utcNow);

    LoginFailureOutcome RecordFailure(string email, string? ip, DateTime utcNow);

    // The right password: the account's run of wrong ones is over.
    void RecordSuccess(string email);

    // A password reset: whatever locked the account is moot now.
    void ClearAccount(string email);
}

public sealed class LoginGuard : ILoginGuard
{
    public const int AccountThreshold = 5;
    public const int IpThreshold = 10;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan AccountLock = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan IpBlock = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan IpBlockRepeat = TimeSpan.FromHours(1);

    private const int SweepAbove = 10_000;
    private static readonly TimeZoneInfo Myt = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kuala_Lumpur");

    private sealed class AccountEntry
    {
        public readonly Queue<DateTime> Failures = new();
        public DateTime? LockedUntil;
    }

    private sealed class IpEntry
    {
        public readonly Queue<(DateTime At, string Email)> Failures = new();
        public DateTime? BlockedUntil;
        public DateOnly? LastBlockedOn;
    }

    private readonly ConcurrentDictionary<string, AccountEntry> _accounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IpEntry> _ips = new(StringComparer.Ordinal);

    public LoginBlock? Check(string email, string? ip, DateTime utcNow)
    {
        DateTime? account = null, address = null;
        if (_accounts.TryGetValue(Key(email), out var a))
            lock (a) if (a.LockedUntil > utcNow) account = a.LockedUntil;
        if (ip is not null && _ips.TryGetValue(ip, out var i))
            lock (i) if (i.BlockedUntil > utcNow) address = i.BlockedUntil;

        // The one that lasts longer is the one worth saying.
        if (account is null && address is null) return null;
        return (account ?? DateTime.MinValue) >= (address ?? DateTime.MinValue)
            ? new LoginBlock(true, account!.Value)
            : new LoginBlock(false, address!.Value);
    }

    public LoginFailureOutcome RecordFailure(string email, string? ip, DateTime utcNow)
    {
        if (_accounts.Count + _ips.Count > SweepAbove) Sweep(utcNow);

        TimeSpan? accountLocked = null;
        var a = _accounts.GetOrAdd(Key(email), _ => new AccountEntry());
        lock (a)
        {
            Prune(a.Failures, utcNow);
            a.Failures.Enqueue(utcNow);
            if (a.Failures.Count >= AccountThreshold)
            {
                a.LockedUntil = utcNow + AccountLock;
                a.Failures.Clear();
                accountLocked = AccountLock;
            }
        }

        TimeSpan? ipBlocked = null;
        var again = false;
        IReadOnlyList<string> emails = [];
        if (ip is not null)
        {
            var i = _ips.GetOrAdd(ip, _ => new IpEntry());
            lock (i)
            {
                while (i.Failures.Count > 0 && utcNow - i.Failures.Peek().At > Window) i.Failures.Dequeue();
                i.Failures.Enqueue((utcNow, Key(email)));
                if (i.Failures.Count >= IpThreshold)
                {
                    var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utcNow, Myt));
                    again = i.LastBlockedOn == today;
                    ipBlocked = again ? IpBlockRepeat : IpBlock;
                    i.BlockedUntil = utcNow + ipBlocked.Value;
                    i.LastBlockedOn = today;
                    emails = [.. i.Failures.Select(f => f.Email).Distinct()];
                    i.Failures.Clear();
                }
            }
        }

        return accountLocked is null && ipBlocked is null
            ? LoginFailureOutcome.Nothing
            : new LoginFailureOutcome(accountLocked, ipBlocked, again, emails);
    }

    public void RecordSuccess(string email)
    {
        if (_accounts.TryGetValue(Key(email), out var a))
            lock (a) a.Failures.Clear();
    }

    public void ClearAccount(string email) => _accounts.TryRemove(Key(email), out _);

    private static string Key(string email) => email.Trim().ToLowerInvariant();

    private static void Prune(Queue<DateTime> failures, DateTime utcNow)
    {
        while (failures.Count > 0 && utcNow - failures.Peek() > Window) failures.Dequeue();
    }

    // Drops what no longer counts for anything. An IP keeps its entry for the
    // rest of the day after a block, so a second block that day is the longer one.
    private void Sweep(DateTime utcNow)
    {
        foreach (var (key, a) in _accounts)
        {
            bool stale;
            lock (a)
            {
                Prune(a.Failures, utcNow);
                stale = a.Failures.Count == 0 && !(a.LockedUntil > utcNow);
            }
            if (stale) _accounts.TryRemove(key, out _);
        }

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utcNow, Myt));
        foreach (var (key, i) in _ips)
        {
            bool stale;
            lock (i)
            {
                while (i.Failures.Count > 0 && utcNow - i.Failures.Peek().At > Window) i.Failures.Dequeue();
                stale = i.Failures.Count == 0 && !(i.BlockedUntil > utcNow) && i.LastBlockedOn != today;
            }
            if (stale) _ips.TryRemove(key, out _);
        }
    }
}

// Thrown by AuthService when a lock or block is in force; the controller
// turns it into a 429 with Retry-After.
public sealed class LoginBlockedException(LoginBlock block, DateTime utcNow) : Exception(MessageFor(block, utcNow))
{
    public LoginBlock Block { get; } = block;
    public TimeSpan RetryAfter { get; } = block.UntilUtc - utcNow;

    private static string MessageFor(LoginBlock block, DateTime utcNow)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling((block.UntilUtc - utcNow).TotalMinutes));
        var wait = minutes == 1 ? "1 minute" : $"{minutes} minutes";
        return block.ForAccount
            ? $"Too many wrong passwords for this account. Try again in {wait}, or reset your password."
            : $"Too many wrong passwords from your network. Try again in {wait}.";
    }
}
