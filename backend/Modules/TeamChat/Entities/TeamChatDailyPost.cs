namespace AltomateHR.Api.Modules.TeamChat.Entities;

// One row per day the daily summary was posted (the Malaysian date it was
// posted on). Its key is the guard: claiming a day twice fails, so a restart
// in the 18:00 hour — or a second server — doesn't post it again.
// Not tenant-scoped: it's about the whole system, not a company.
public class TeamChatDailyPost
{
    public DateOnly Day { get; set; }
    public DateTime PostedAt { get; set; }
}
