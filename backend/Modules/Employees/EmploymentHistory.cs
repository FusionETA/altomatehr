using AltomateHR.Api.Modules.Employees.Dtos;
using AltomateHR.Api.Modules.Employees.Entities;

namespace AltomateHR.Api.Modules.Employees;

// Records each tenure as it ends or begins again — see EmploymentPeriod. The
// Close/Open calls only STAGE rows; whoever made the change (profile save,
// transfer, sweep) commits them with it in one SaveChanges.
public interface IEmploymentHistory
{
    // A tenure ended. Closes the open period, or — for someone with no rows
    // yet (everyone hired before this existed) — writes the whole period from
    // `joinDate`.
    Task CloseAsync(
        string organizationId, string userId, DateTime? joinDate, DateTime? leaveDate,
        string endReason, string? transferId = null);

    // A new tenure began (restore, transfer in).
    Task OpenAsync(
        string organizationId, string userId, DateTime? joinDate, string startReason, string? transferId = null);

    // Before an archived profile is reopened (restore, transfer back) its dates
    // are overwritten. If that ended tenure was never recorded — it predates
    // this history — write it now so the overwrite loses nothing.
    Task RecordEndedIfMissingAsync(
        string organizationId, string userId, DateTime? joinDate, DateTime? leaveDate, string? endReason);

    // Every tenure in the CURRENT org, newest first. Someone with no rows gets
    // one synthesised current period from their profile. Null → not a member here.
    Task<IReadOnlyList<EmploymentPeriodDto>?> GetAsync(string userId);
}

public class EmploymentHistory : IEmploymentHistory
{
    private readonly IEmploymentPeriodRepository _periods;
    private readonly IEmployeeProfileRepository _profiles;
    private readonly IOrganizationMembershipRepository _memberships;

    public EmploymentHistory(
        IEmploymentPeriodRepository periods,
        IEmployeeProfileRepository profiles,
        IOrganizationMembershipRepository memberships)
    {
        _periods = periods;
        _profiles = profiles;
        _memberships = memberships;
    }

    public async Task CloseAsync(
        string organizationId, string userId, DateTime? joinDate, DateTime? leaveDate,
        string endReason, string? transferId = null)
    {
        var open = await _periods.GetOpenAsync(organizationId, userId);
        if (open is null)
        {
            open = new EmploymentPeriod { OrganizationId = organizationId, UserId = userId, JoinDate = joinDate?.Date };
            _periods.Stage(open);
        }

        // An archive with no last day still ends the tenure; record the day it happened.
        open.LeaveDate = (leaveDate ?? DateTime.UtcNow).Date;
        open.EndReason = Truncate(endReason);
        open.ClosedByTransferId = transferId;
        open.UpdatedAt = DateTime.UtcNow;
    }

    public async Task OpenAsync(
        string organizationId, string userId, DateTime? joinDate, string startReason, string? transferId = null)
    {
        // Never two open periods: one left open by a missed close ends where this begins.
        var stale = await _periods.GetOpenAsync(organizationId, userId);
        if (stale is not null)
        {
            stale.LeaveDate = (joinDate ?? DateTime.UtcNow).Date.AddDays(-1);
            stale.EndReason ??= "Ended";
            stale.UpdatedAt = DateTime.UtcNow;
        }

        _periods.Stage(new EmploymentPeriod
        {
            OrganizationId = organizationId,
            UserId = userId,
            JoinDate = joinDate?.Date,
            StartReason = Truncate(startReason),
            OpenedByTransferId = transferId,
        });
    }

    public async Task RecordEndedIfMissingAsync(
        string organizationId, string userId, DateTime? joinDate, DateTime? leaveDate, string? endReason)
    {
        if (leaveDate is null && joinDate is null) return;
        if (leaveDate is not null && await _periods.HasPeriodEndingAsync(organizationId, userId, leaveDate.Value)) return;

        // An open row means history already covers this tenure; CloseAsync ends it.
        await CloseAsync(organizationId, userId, joinDate, leaveDate, endReason ?? "Archived");
    }

    public async Task<IReadOnlyList<EmploymentPeriodDto>?> GetAsync(string userId)
    {
        var membership = await _memberships.GetForUserInCurrentOrgAsync(userId);
        if (membership is null) return null;

        var rows = await _periods.GetForUserAsync(userId);
        var result = rows.Select(p => new EmploymentPeriodDto
        {
            JoinDate = p.JoinDate,
            LeaveDate = p.LeaveDate,
            StartReason = p.StartReason,
            EndReason = p.EndReason,
            IsCurrent = p.LeaveDate is null,
        }).ToList();

        // No open row but still employed here (never left, or hired before
        // history existed): the profile IS the current period.
        if (result.All(r => !r.IsCurrent))
        {
            var profile = await _profiles.GetByUserAsync(userId);
            if (profile is null || !profile.IsArchived)
            {
                var joined = profile?.JoinDate ?? membership?.JoinDate;
                if (joined is not null || result.Count == 0)
                {
                    result.Insert(0, new EmploymentPeriodDto { JoinDate = joined, IsCurrent = true });
                }
            }
        }

        return result;
    }

    private static string Truncate(string s) => s.Length > 200 ? s[..200] : s;
}
