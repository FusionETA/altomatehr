using System.Globalization;
using Microsoft.Extensions.Options;

namespace AltomateHR.Api.Modules.TeamChat;

public interface ITeamChatDailySummaryService
{
    // Posts yesterday-of-`today` (Malaysian dates) to the daily chat. Returns
    // how many messages were queued: 0 when no daily chat is set up.
    Task<int> PostAsync(DateOnly today);
}

// The 08:00 message in the daily chat: what each company did the day before.
// Companies with nothing to say are counted in one line, not listed.
public class TeamChatDailySummaryService : ITeamChatDailySummaryService
{
    private static readonly TimeZoneInfo Myt = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kuala_Lumpur");

    // Bitrix takes long messages, but a chat reads better in pieces.
    public const int MaxMessageLength = 3_500;

    private readonly ITeamChatActivityRepository _activity;
    private readonly ITeamChatQueue _queue;
    private readonly TeamChatOptions _options;
    private readonly ILogger<TeamChatDailySummaryService> _logger;

    public TeamChatDailySummaryService(
        ITeamChatActivityRepository activity,
        ITeamChatQueue queue,
        IOptions<TeamChatOptions> options,
        ILogger<TeamChatDailySummaryService> logger)
    {
        _activity = activity;
        _queue = queue;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<int> PostAsync(DateOnly today)
    {
        if (!_options.IsConfigured(TeamChatChannel.Daily)) return 0;

        var day = today.AddDays(-1);
        var startLocal = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var fromUtc = TimeZoneInfo.ConvertTimeToUtc(startLocal, Myt);
        var toUtc = TimeZoneInfo.ConvertTimeToUtc(startLocal.AddDays(1), Myt);
        var localDay = DateTime.SpecifyKind(startLocal, DateTimeKind.Utc);

        var companies = await _activity.GetAsync(fromUtc, toUtc, localDay);
        var messages = SummaryMessages(day, companies);
        foreach (var text in messages)
        {
            if (!_queue.TryEnqueue(new TeamChatMessage(TeamChatChannel.Daily, text)))
                _logger.LogWarning("Team chat queue is full; part of the daily summary was dropped.");
        }
        return messages.Count;
    }

    public static IReadOnlyList<string> SummaryMessages(DateOnly day, IReadOnlyList<CompanyActivity> companies)
    {
        var active = companies.Where(c => !c.IsQuiet).ToList();
        var quiet = companies.Count - active.Count;

        var blocks = new List<string>
        {
            $"☀️ Daily summary · {day.ToString("ddd d MMM yyyy", CultureInfo.InvariantCulture)}"
            + $"\n{Count(active.Count, "company", "companies")} active · {quiet} quiet",
        };
        if (active.Count > 0)
        {
            blocks.Add(TotalsBlock(active));
            blocks.AddRange(active.Select(CompanyBlock));
        }

        // Whole company blocks per message, never one cut in half.
        var messages = new List<string>();
        var current = "";
        foreach (var block in blocks)
        {
            if (current.Length > 0 && current.Length + 2 + block.Length > MaxMessageLength)
            {
                messages.Add(current);
                current = "";
            }
            current = current.Length == 0 ? block : $"{current}\n\n{block}";
        }
        if (current.Length > 0) messages.Add(current);
        return messages;
    }

    private static string TotalsBlock(IReadOnlyList<CompanyActivity> active)
    {
        var all = new CompanyActivity(
            "", "All companies",
            active.Sum(c => c.SignIns), active.Sum(c => c.PeopleSignedIn),
            active.Sum(c => c.RunsApproved), active.Sum(c => c.RunsSentForApproval),
            active.Sum(c => c.LeaveApplied), active.Sum(c => c.LeaveApproved), active.Sum(c => c.LeaveWaiting),
            active.Sum(c => c.ClaimsSubmitted), active.Sum(c => c.ClaimsWaiting),
            active.Sum(c => c.PeopleClockedIn), active.Sum(c => c.AttendanceWaiting));
        return CompanyBlock(all);
    }

    private static string CompanyBlock(CompanyActivity c)
    {
        var lines = new List<string> { $"🏢 {TeamChatAlerts.Clean(c.Company)}" };
        if (c.SignIns > 0)
            lines.Add($"• Sign-ins: {c.SignIns} ({Count(c.PeopleSignedIn, "person", "people")})");
        if (c.RunsApproved > 0 || c.RunsSentForApproval > 0)
            lines.Add($"• Payroll: {Join(Part(c.RunsApproved, "run approved", "runs approved"), Part(c.RunsSentForApproval, "sent for approval", "sent for approval"))}");
        if (c.LeaveApplied > 0 || c.LeaveApproved > 0)
            lines.Add($"• Leave: {Join(Part(c.LeaveApplied, "applied", "applied"), Part(c.LeaveApproved, "approved", "approved"), Part(c.LeaveWaiting, "waiting", "waiting"))}");
        if (c.ClaimsSubmitted > 0)
            lines.Add($"• Claims: {Join(Part(c.ClaimsSubmitted, "submitted", "submitted"), Part(c.ClaimsWaiting, "waiting", "waiting"))}");
        if (c.PeopleClockedIn > 0)
            lines.Add($"• Attendance: {Join($"{Count(c.PeopleClockedIn, "person", "people")} clocked in", Part(c.AttendanceWaiting, "waiting approval", "waiting approval"))}");
        return string.Join("\n", lines);
    }

    private static string? Part(int n, string one, string many) => n == 0 ? null : $"{n} {(n == 1 ? one : many)}";

    private static string Join(params string?[] parts) => string.Join(" · ", parts.Where(p => p is not null));

    private static string Count(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";
}
