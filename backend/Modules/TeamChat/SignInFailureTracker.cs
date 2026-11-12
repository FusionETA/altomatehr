using System.Collections.Concurrent;

namespace AltomateHR.Api.Modules.TeamChat;

// Counts wrong passwords per account, in memory, so the alert chat hears of
// someone hammering an account once — not on every typo. A restart forgets
// the counts, which costs at most one missed alert.
public interface ISignInFailureTracker
{
    // Records a failure; true when this one should be said in the chat.
    bool RecordAndShouldAlert(string key, DateTime utcNow);
}

public sealed class SignInFailureTracker : ISignInFailureTracker
{
    public const int Threshold = 5;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    // Above this many accounts tracked, stale ones are swept on the next record.
    private const int SweepAbove = 5_000;

    private sealed class Entry
    {
        public readonly Queue<DateTime> Failures = new();
        public DateTime? AlertedAt;
    }

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public bool RecordAndShouldAlert(string key, DateTime utcNow)
    {
        if (_entries.Count > SweepAbove) Sweep(utcNow);

        var entry = _entries.GetOrAdd(key, _ => new Entry());
        lock (entry)
        {
            entry.Failures.Enqueue(utcNow);
            while (entry.Failures.Count > 0 && utcNow - entry.Failures.Peek() > Window)
                entry.Failures.Dequeue();

            // Once per window: further failures straight after don't repeat it.
            if (entry.Failures.Count < Threshold) return false;
            if (entry.AlertedAt is { } at && utcNow - at <= Window) return false;
            entry.AlertedAt = utcNow;
            return true;
        }
    }

    private void Sweep(DateTime utcNow)
    {
        foreach (var (key, entry) in _entries)
        {
            bool stale;
            lock (entry)
            {
                stale = (entry.Failures.Count == 0 || utcNow - entry.Failures.Last() > Window)
                        && (entry.AlertedAt is null || utcNow - entry.AlertedAt > Window);
            }
            if (stale) _entries.TryRemove(key, out _);
        }
    }
}
