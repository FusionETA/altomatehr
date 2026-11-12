namespace AltomateHR.Api.Modules.TeamChat;

// One company's day, for the 08:00 summary in the daily chat. The "Waiting"
// counts are as of now, not of the day.
public sealed record CompanyActivity(
    string OrganizationId,
    string Company,
    int SignIns,
    int PeopleSignedIn,
    int RunsApproved,
    int RunsSentForApproval,
    int LeaveApplied,
    int LeaveApproved,
    int LeaveWaiting,
    int ClaimsSubmitted,
    int ClaimsWaiting,
    int PeopleClockedIn,
    int AttendanceWaiting)
{
    public bool IsQuiet =>
        SignIns == 0 && RunsApproved == 0 && RunsSentForApproval == 0 && LeaveApplied == 0
        && LeaveApproved == 0 && ClaimsSubmitted == 0 && PeopleClockedIn == 0;
}

// Reads across every company — it runs from a background job with no
// signed-in user, so no tenant filter applies.
public interface ITeamChatActivityRepository
{
    // Active (not deactivated) companies only, whether or not anything happened.
    // fromUtc/toUtc bound the day; localDay is its date as attendance stores it
    // (UTC midnight of the Malaysian date).
    Task<IReadOnlyList<CompanyActivity>> GetAsync(DateTime fromUtc, DateTime toUtc, DateTime localDay);

    // Marks `day` as posted. False when it already was (by this server before
    // a restart, or by another one).
    Task<bool> TryClaimDayAsync(DateOnly day, DateTime utcNow);
}
