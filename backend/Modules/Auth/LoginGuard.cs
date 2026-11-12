using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;

namespace AltomateHR.Api.Modules.Auth;

// Why a sign-in was refused before the password was even checked. InProgress:
// not a lock — as many attempts are being checked right now as would lock it,
// so this one waits a moment.
public sealed record LoginBlock(bool ForAccount, DateTime UntilUtc, bool InProgress = false);

// One sign-in attempt, held "in progress" from Begin until Fail, Succeed or
// Abandon resolves it. Block is set when it was refused outright.
public sealed class LoginAttempt
{
    internal LoginAttempt(string account, string email, string? ip, LoginBlock? block)
    {
        Account = account;
        Email = email;
        Ip = ip;
        Block = block;
    }

    internal string Account { get; }
    internal string Email { get; }
    internal string? Ip { get; }
    internal bool Resolved { get; set; }
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
//     The account is the one the email MATCHED (UserKey), so no spelling the
//     database treats as the same email — case, accents, invisible characters,
//     ß for ss — gets a fresh count. An email matching nobody is counted by its
//     folded text (EmailKey), so a lock says nothing about which emails exist.
//
//   • An IP is blocked from signing in for 15 minutes after 10 wrong passwords
//     within 15 minutes, whoever they were for; 1 hour if it was already
//     blocked earlier the same (Malaysian) day. A success does NOT reset it,
//     or a guesser could sign in to their own account between tries. Short,
//     because outlet staff share one wifi IP and phones share carrier IPs.
//     An IPv6 client is counted by its /64, which one client holds whole.
//
// Attempts being checked right now are held apart from wrong ones. Begin
// refuses once wrong + in progress would reach a limit — a burst of parallel
// guesses can't all get past before any is counted — but only wrong ones lock
// or block, so a colleague's right password in progress never tips the IP.
//
// In memory: one server, and a restart only forgets counts and blocks, which
// errs towards letting people in.
public interface ILoginGuard
{
    // Starts an attempt on `account` (UserKey or EmailKey); `email` is as typed,
    // for the alert's list of what an IP tried. Its Block is set, and nothing
    // is held, when the account or this IP may not try now. `ip` null skips
    // the IP rule.
    LoginAttempt Begin(string account, string email, string? ip, DateTime utcNow);

    // The password was wrong: locks or blocks when the wrong ones reach a limit.
    LoginFailureOutcome Fail(LoginAttempt attempt, DateTime utcNow);

    // The password was right: the account's run of wrong ones is over.
    void Succeed(LoginAttempt attempt);

    // Neither — something failed before the password was judged. The attempt
    // is let go without counting either way.
    void Abandon(LoginAttempt attempt);

    // A password reset: whatever locked the account is moot now.
    void ClearAccount(string account);
}

public sealed class LoginGuard : ILoginGuard
{
    public const int AccountThreshold = 5;
    public const int IpThreshold = 10;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan AccountLock = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan IpBlock = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan IpBlockRepeat = TimeSpan.FromHours(1);
    public static readonly TimeSpan InProgressWait = TimeSpan.FromSeconds(5);

    private const int SweepAbove = 10_000;
    private static readonly TimeZoneInfo Myt = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kuala_Lumpur");

    private sealed class AccountEntry
    {
        public readonly List<DateTime> Wrong = [];
        public int InProgress;
        public DateTime? LockedUntil;
    }

    private sealed class IpEntry
    {
        public readonly List<(DateTime At, string Email)> Wrong = [];
        public int InProgress;
        public DateTime? BlockedUntil;
        public DateOnly? LastBlockedOn;
    }

    private readonly ConcurrentDictionary<string, AccountEntry> _accounts = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IpEntry> _ips = new(StringComparer.Ordinal);

    // The account an email matched — every spelling of it shares this.
    public static string UserKey(string userId) => $"user:{userId}";

    // An email that matched nobody, folded roughly as the database compares.
    public static string EmailKey(string email)
    {
        var folded = email.Trim().Normalize(NormalizationForm.FormKD);
        var plain = new StringBuilder(folded.Length);
        foreach (var c in folded)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.Format) continue;
            plain.Append(c);
        }
        return $"email:{plain.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant()}";
    }

    // IPv4 as it is; IPv6 by its /64 — one client holds the whole prefix and
    // could otherwise take a fresh address for every guess. Anything that
    // isn't an IP (a per-key bucket) is used as it is.
    public static string IpKey(string ip)
    {
        if (!IPAddress.TryParse(ip, out var address)) return ip;
        if (address.IsIPv4MappedToIPv6) return address.MapToIPv4().ToString();
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6) return address.ToString();

        var bytes = address.GetAddressBytes();
        for (var b = 8; b < 16; b++) bytes[b] = 0;
        return $"{new IPAddress(bytes)}/64";
    }

    public LoginAttempt Begin(string account, string email, string? ip, DateTime utcNow)
    {
        if (_accounts.Count + _ips.Count > SweepAbove) Sweep(utcNow);

        var ipKey = ip is null ? null : IpKey(ip);
        var a = _accounts.GetOrAdd(account, _ => new AccountEntry());
        var i = ipKey is null ? null : _ips.GetOrAdd(ipKey, _ => new IpEntry());

        // Both entries are checked and held together, account first, so the
        // order of locks is the same everywhere.
        lock (a)
        {
            var email1 = email.Trim();
            if (i is null) return Hold(a, null, account, email1, null, utcNow);
            lock (i) return Hold(a, i, account, email1, ipKey, utcNow);
        }
    }

    private static LoginAttempt Hold(
        AccountEntry a, IpEntry? i, string account, string email, string? ipKey, DateTime utcNow)
    {
        PruneAccount(a, utcNow);
        if (i is not null) PruneIp(i, utcNow);

        LoginAttempt Refused(LoginBlock block) => new(account, email, ipKey, block);

        if (a.LockedUntil > utcNow) return Refused(new LoginBlock(true, a.LockedUntil.Value));
        if (i?.BlockedUntil > utcNow) return Refused(new LoginBlock(false, i.BlockedUntil.Value));

        if (a.Wrong.Count + a.InProgress >= AccountThreshold)
            return Refused(new LoginBlock(true, utcNow + InProgressWait, InProgress: true));
        if (i is not null && i.Wrong.Count + i.InProgress >= IpThreshold)
            return Refused(new LoginBlock(false, utcNow + InProgressWait, InProgress: true));

        a.InProgress++;
        if (i is not null) i.InProgress++;
        return new LoginAttempt(account, email, ipKey, null);
    }

    public LoginFailureOutcome Fail(LoginAttempt attempt, DateTime utcNow)
    {
        if (!Resolve(attempt)) return LoginFailureOutcome.Nothing;

        TimeSpan? accountLocked = null;
        if (_accounts.TryGetValue(attempt.Account, out var a))
        {
            lock (a)
            {
                a.InProgress = Math.Max(0, a.InProgress - 1);
                PruneAccount(a, utcNow);
                a.Wrong.Add(utcNow);
                if (a.Wrong.Count >= AccountThreshold && !(a.LockedUntil > utcNow))
                {
                    a.LockedUntil = utcNow + AccountLock;
                    a.Wrong.Clear();
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
                i.InProgress = Math.Max(0, i.InProgress - 1);
                PruneIp(i, utcNow);
                i.Wrong.Add((utcNow, attempt.Email));
                if (i.Wrong.Count >= IpThreshold && !(i.BlockedUntil > utcNow))
                {
                    var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utcNow, Myt));
                    again = i.LastBlockedOn == today;
                    ipBlocked = again ? IpBlockRepeat : IpBlock;
                    i.BlockedUntil = utcNow + ipBlocked.Value;
                    i.LastBlockedOn = today;
                    emails = [.. i.Wrong.Select(x => x.Email).Distinct(StringComparer.OrdinalIgnoreCase)];
                    i.Wrong.Clear();
                }
            }
        }

        return accountLocked is null && ipBlocked is null
            ? LoginFailureOutcome.Nothing
            : new LoginFailureOutcome(accountLocked, ipBlocked, again, emails);
    }

    public void Succeed(LoginAttempt attempt)
    {
        if (!Resolve(attempt)) return;
        Release(attempt, clearAccount: true);
    }

    public void Abandon(LoginAttempt attempt)
    {
        if (!Resolve(attempt)) return;
        Release(attempt, clearAccount: false);
    }

    public void ClearAccount(string account) => _accounts.TryRemove(account, out _);

    // Once per attempt, and never for one that was refused (it held nothing).
    private static bool Resolve(LoginAttempt attempt)
    {
        lock (attempt)
        {
            if (attempt.Block is not null || attempt.Resolved) return false;
            attempt.Resolved = true;
            return true;
        }
    }

    private void Release(LoginAttempt attempt, bool clearAccount)
    {
        if (_accounts.TryGetValue(attempt.Account, out var a))
        {
            lock (a)
            {
                a.InProgress = Math.Max(0, a.InProgress - 1);
                if (clearAccount) a.Wrong.Clear();
            }
        }
        if (attempt.Ip is not null && _ips.TryGetValue(attempt.Ip, out var i))
            lock (i) i.InProgress = Math.Max(0, i.InProgress - 1);
    }

    private static void PruneAccount(AccountEntry a, DateTime utcNow) =>
        a.Wrong.RemoveAll(at => utcNow - at > Window);

    private static void PruneIp(IpEntry i, DateTime utcNow) =>
        i.Wrong.RemoveAll(x => utcNow - x.At > Window);

    // Drops what no longer counts for anything — never an entry with an
    // attempt in progress. An IP keeps its entry for the rest of the day after
    // a block, so a second block that day is the longer one.
    private void Sweep(DateTime utcNow)
    {
        foreach (var (key, a) in _accounts)
        {
            lock (a)
            {
                PruneAccount(a, utcNow);
                if (a.Wrong.Count == 0 && a.InProgress == 0 && !(a.LockedUntil > utcNow))
                    _accounts.TryRemove(key, out _);
            }
        }

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utcNow, Myt));
        foreach (var (key, i) in _ips)
        {
            lock (i)
            {
                PruneIp(i, utcNow);
                if (i.Wrong.Count == 0 && i.InProgress == 0 && !(i.BlockedUntil > utcNow) && i.LastBlockedOn != today)
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
        if (block.InProgress)
        {
            return block.ForAccount
                ? "Another sign-in to this account is still being checked. Try again in a moment."
                : "Too many sign-ins are being checked from your network. Try again in a moment.";
        }

        var minutes = Math.Max(1, (int)Math.Ceiling((block.UntilUtc - utcNow).TotalMinutes));
        var wait = minutes == 1 ? "1 minute" : $"{minutes} minutes";
        return block.ForAccount
            ? $"Too many wrong passwords for this account. Try again in {wait}, or reset your password."
            : $"Too many wrong passwords from your network. Try again in {wait}.";
    }
}
