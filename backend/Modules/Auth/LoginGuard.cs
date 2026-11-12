using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;

namespace AltomateHR.Api.Modules.Auth;

// Why a sign-in was refused before the password was even checked.
public sealed record LoginBlock(bool ForAccount, DateTime UntilUtc);

// One sign-in attempt, counted as a wrong password from the moment it starts
// (see LoginGuard.Begin) until Succeed takes it back. Block is set when it
// was refused outright.
public sealed class LoginAttempt
{
    internal LoginAttempt(string account, string? ip, DateTime at, LoginBlock? block)
    {
        Account = account;
        Ip = ip;
        At = at;
        Block = block;
    }

    internal string Account { get; }
    internal string? Ip { get; }
    internal DateTime At { get; }
    public LoginBlock? Block { get; }
}

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
//     An IPv6 client is counted by its /64, which one client holds whole.
//
// Every attempt counts as wrong from the moment it STARTS, and a right
// password takes it back. Counting only once BCrypt has answered let a burst
// of parallel requests all pass the check before any of them was recorded.
//
// In memory: one server, and a restart only forgets counts and blocks, which
// errs towards letting people in.
public interface ILoginGuard
{
    // Starts an attempt. Its Block is set (and nothing is counted) when the
    // account, or this IP, may not try now. `ip` null skips the IP rule.
    LoginAttempt Begin(string email, string? ip, DateTime utcNow);

    // The password was wrong: locks or blocks when this one reaches a limit.
    LoginFailureOutcome Fail(LoginAttempt attempt, DateTime utcNow);

    // The password was right: the attempt is taken back, and the account's
    // run of wrong ones is over.
    void Succeed(LoginAttempt attempt);

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
        public readonly List<DateTime> Attempts = [];
        public DateTime? LockedUntil;
    }

    private sealed class IpEntry
    {
        public readonly List<(DateTime At, string Account)> Attempts = [];
        public DateTime? BlockedUntil;
        public DateOnly? LastBlockedOn;
    }

    private readonly ConcurrentDictionary<string, AccountEntry> _accounts = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IpEntry> _ips = new(StringComparer.Ordinal);

    public LoginAttempt Begin(string email, string? ip, DateTime utcNow)
    {
        if (_accounts.Count + _ips.Count > SweepAbove) Sweep(utcNow);

        var account = AccountKey(email);
        var ipKey = ip is null ? null : IpKey(ip);
        var a = _accounts.GetOrAdd(account, _ => new AccountEntry());
        var i = ipKey is null ? null : _ips.GetOrAdd(ipKey, _ => new IpEntry());

        // Both entries are checked and claimed together, account first, so the
        // order of locks is the same everywhere.
        lock (a)
        {
            if (i is null) return Claim(a, null, account, null, utcNow);
            lock (i) return Claim(a, i, account, ipKey, utcNow);
        }
    }

    private static LoginAttempt Claim(
        AccountEntry a, IpEntry? i, string account, string? ipKey, DateTime utcNow)
    {
        Prune(a.Attempts, utcNow);
        if (i is not null) i.Attempts.RemoveAll(x => utcNow - x.At > Window);

        DateTime? accountUntil = a.LockedUntil > utcNow ? a.LockedUntil : null;
        DateTime? ipUntil = i?.BlockedUntil > utcNow ? i.BlockedUntil : null;

        // As many attempts are already in flight as would lock it: this one
        // waits, whatever they turn out to be.
        if (accountUntil is null && a.Attempts.Count >= AccountThreshold) accountUntil = utcNow + AccountLock;
        if (ipUntil is null && i is not null && i.Attempts.Count >= IpThreshold) ipUntil = utcNow + IpBlock;

        if (accountUntil is not null || ipUntil is not null)
        {
            // The one that lasts longer is the one worth saying.
            var block = (accountUntil ?? DateTime.MinValue) >= (ipUntil ?? DateTime.MinValue)
                ? new LoginBlock(true, accountUntil!.Value)
                : new LoginBlock(false, ipUntil!.Value);
            return new LoginAttempt(account, ipKey, utcNow, block);
        }

        a.Attempts.Add(utcNow);
        i?.Attempts.Add((utcNow, account));
        return new LoginAttempt(account, ipKey, utcNow, null);
    }

    public LoginFailureOutcome Fail(LoginAttempt attempt, DateTime utcNow)
    {
        if (attempt.Block is not null) return LoginFailureOutcome.Nothing;

        TimeSpan? accountLocked = null;
        if (_accounts.TryGetValue(attempt.Account, out var a))
        {
            lock (a)
            {
                Prune(a.Attempts, utcNow);
                if (a.Attempts.Count >= AccountThreshold && !(a.LockedUntil > utcNow))
                {
                    a.LockedUntil = utcNow + AccountLock;
                    a.Attempts.Clear();
                    accountLocked = AccountLock;
                }
            }
        }

        TimeSpan? ipBlocked = null;
        var again = false;
        IReadOnlyList<string> emails = [];
        if (attempt.Ip is not null && _ips.TryGetValue(attempt.Ip, out var i))
        {
            lock (i)
            {
                i.Attempts.RemoveAll(x => utcNow - x.At > Window);
                if (i.Attempts.Count >= IpThreshold && !(i.BlockedUntil > utcNow))
                {
                    var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utcNow, Myt));
                    again = i.LastBlockedOn == today;
                    ipBlocked = again ? IpBlockRepeat : IpBlock;
                    i.BlockedUntil = utcNow + ipBlocked.Value;
                    i.LastBlockedOn = today;
                    emails = [.. i.Attempts.Select(x => x.Account).Distinct()];
                    i.Attempts.Clear();
                }
            }
        }

        return accountLocked is null && ipBlocked is null
            ? LoginFailureOutcome.Nothing
            : new LoginFailureOutcome(accountLocked, ipBlocked, again, emails);
    }

    public void Succeed(LoginAttempt attempt)
    {
        if (attempt.Block is not null) return;

        if (_accounts.TryGetValue(attempt.Account, out var a))
            lock (a) a.Attempts.Clear();

        if (attempt.Ip is not null && _ips.TryGetValue(attempt.Ip, out var i))
        {
            lock (i)
            {
                var index = i.Attempts.FindIndex(x => x.At == attempt.At && x.Account == attempt.Account);
                if (index >= 0) i.Attempts.RemoveAt(index);
            }
        }
    }

    public void ClearAccount(string email) => _accounts.TryRemove(AccountKey(email), out _);

    // The email as the database would match it: its collation ignores case,
    // accents and full/half width, so "ádmin@" and "ａdmin@" find the same
    // account as "admin@" — and must count against the same lock.
    public static string AccountKey(string email)
    {
        var folded = email.Trim().Normalize(NormalizationForm.FormKD);
        var plain = new StringBuilder(folded.Length);
        foreach (var c in folded)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) plain.Append(c);
        }
        return plain.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }

    // IPv4 as it is; IPv6 by its /64 — one client holds the whole prefix and
    // could otherwise take a fresh address for every guess.
    public static string IpKey(string ip)
    {
        if (!IPAddress.TryParse(ip, out var address)) return ip;
        if (address.IsIPv4MappedToIPv6) return address.MapToIPv4().ToString();
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6) return address.ToString();

        var bytes = address.GetAddressBytes();
        for (var b = 8; b < 16; b++) bytes[b] = 0;
        return $"{new IPAddress(bytes)}/64";
    }

    private static void Prune(List<DateTime> attempts, DateTime utcNow) =>
        attempts.RemoveAll(at => utcNow - at > Window);

    // Drops what no longer counts for anything. An IP keeps its entry for the
    // rest of the day after a block, so a second block that day is the longer one.
    private void Sweep(DateTime utcNow)
    {
        foreach (var (key, a) in _accounts)
        {
            lock (a)
            {
                Prune(a.Attempts, utcNow);
                if (a.Attempts.Count == 0 && !(a.LockedUntil > utcNow)) _accounts.TryRemove(key, out _);
            }
        }

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utcNow, Myt));
        foreach (var (key, i) in _ips)
        {
            lock (i)
            {
                i.Attempts.RemoveAll(x => utcNow - x.At > Window);
                if (i.Attempts.Count == 0 && !(i.BlockedUntil > utcNow) && i.LastBlockedOn != today)
                    _ips.TryRemove(key, out _);
            }
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
