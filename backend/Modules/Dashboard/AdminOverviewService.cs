using AltomateHR.Api.Modules.Claims;
using AltomateHR.Api.Modules.Claims.Entities;
using AltomateHR.Api.Modules.Dashboard.Dtos;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Projects;
using AltomateHR.Api.Modules.Teams;

namespace AltomateHR.Api.Modules.Dashboard;

// Computes the admin executive overview by aggregating org-wide data server-side (the
// repos are tenant-filtered, so this is scoped to the active org). Each card is gated by
// the caller's enabled modules — no claims module ⇒ no claims analytics, etc. Cards are
// added one at a time; only the ones implemented below return data.
public class AdminOverviewService : IAdminOverviewService
{
    private const ApprovalModule Module = ApprovalModule.CLAIMS;

    // A claim pending this long is late enough that someone should be asked about
    // it. Matches the "> 7 days" the admin dashboard promises.
    private const int StaleAfterDays = 7;

    // How far back the approval-trust card looks. Long enough to see a pattern,
    // short enough that a supervisor isn't judged on last year's behaviour.
    private const int OverturnedWindowDays = 90;

    // Named approvers shown on the trust card. It exists to start a conversation,
    // so it lists the few worth talking to rather than ranking everybody.
    private const int OverturnedSampleSize = 5;

    private readonly IClaimsService _claims;
    private readonly IProjectService _projects;
    private readonly IModuleAccessService _modules;
    private readonly IApprovalRouter _router;
    private readonly IEmployeeRowResolver _employees;

    public AdminOverviewService(
        IClaimsService claims,
        IProjectService projects,
        IModuleAccessService modules,
        IApprovalRouter router,
        IEmployeeRowResolver employees)
    {
        _claims = claims;
        _projects = projects;
        _modules = modules;
        _router = router;
        _employees = employees;
    }

    public async Task<AdminOverviewDto> GetAsync()
    {
        // The caller's effective modules = org package ∩ their admin grant.
        var enabled = await _modules.GetEnabledModulesAsync();
        var has = new HashSet<string>(enabled, StringComparer.OrdinalIgnoreCase);

        var dto = new AdminOverviewDto { EnabledModules = enabled.ToList() };

        // Every claims card reads the same org-wide list, so it is fetched once
        // and passed down rather than re-queried per card.
        if (has.Contains(OrgModules.Claims))
        {
            var claims = await _claims.GetAllForOrgAsync();
            dto.ProjectSpend = await ProjectSpendThisMonthAsync(claims);
            dto.StalePendingClaims = await StalePendingClaimsAsync(claims);
            dto.OverturnedSupervisors = await OverturnedSupervisorsAsync(claims);
        }

        // AttendanceHealth (attendance) / SlowOtApprovers (overtime) / UpcomingClaimRun
        // are built next. UpcomingClaimRun additionally needs an org claim-cutoff
        // setting, which does not exist yet — the card renders its empty state.
        return dto;
    }

    // Card 1 — claim spend grouped by project for the current month (excludes rejected).
    private async Task<List<ProjectClaimSpendDto>> ProjectSpendThisMonthAsync(
        IReadOnlyList<Claim> claims)
    {
        var now = DateTime.UtcNow;
        var projectNames = (await _projects.GetAllAsync()).ToDictionary(p => p.Id, p => p.Name);

        return claims
            .Where(c => c.Status != ClaimStatus.REJECTED
                     && c.SubmittedAt.Year == now.Year
                     && c.SubmittedAt.Month == now.Month)
            .GroupBy(c => c.ProjectId)
            .Select(g => new ProjectClaimSpendDto
            {
                Project = g.Key is not null && projectNames.TryGetValue(g.Key, out var name)
                    ? name
                    : "Unassigned",
                TotalAmount = g.Sum(c => c.Amount),
                ClaimCount = g.Count(),
            })
            .OrderByDescending(p => p.TotalAmount)
            .ToList();
    }

    // Card 4 — claims that have been pending longer than StaleAfterDays, oldest
    // first, each labelled with the approver it is currently waiting on. The
    // approver is the point: "3 claims are late" is a statistic, "3 claims are
    // late with Aisha" is something an admin can act on.
    private async Task<List<StalePendingClaimDto>> StalePendingClaimsAsync(
        IReadOnlyList<Claim> claims)
    {
        var now = DateTime.UtcNow;

        var stale = claims
            .Where(c => c.Status == ClaimStatus.PENDING
                     && (now - c.SubmittedAt).TotalDays >= StaleAfterDays)
            .OrderByDescending(c => now - c.SubmittedAt)
            .ToList();

        if (stale.Count == 0) return [];

        var directory = await _employees.GetSnapshotAsync();
        var result = new List<StalePendingClaimDto>(stale.Count);

        foreach (var claim in stale)
        {
            // Who the claim is waiting on right now — the approvers of the step
            // it stalled at, not the whole chain.
            var approvers = await _router.CurrentApproversAsync(
                Module, claim.EmployeeId, claim.CurrentStep);

            result.Add(new StalePendingClaimDto
            {
                Id = claim.Id,
                ClaimNumber = claim.ClaimNumber,
                Title = claim.Title,
                EmployeeName = Label(directory, claim.EmployeeId),
                Amount = claim.Amount,
                DaysPending = (int)(now - claim.SubmittedAt).TotalDays,
                CurrentApprovers = approvers.Select(id => Label(directory, id)).ToList(),
            });
        }

        return result;
    }

    // Card 6 — layer-1 approvers whose approvals a higher layer went on to reject.
    //
    // There is no approval-history table, so "was overturned" is inferred from the
    // claim itself: a REJECTED claim whose CurrentStep advanced past 0 must have
    // been approved at step 0 first, then rejected further up. That also means the
    // chain is read as it stands TODAY — if an employee's approval chain has been
    // changed since, the layer-1 approver named here is the current one, not
    // necessarily the person who signed off. It is a prompt to go and look, which
    // is why the card exposes the claim ids behind every count.
    private async Task<OverturnedSupervisorsDto> OverturnedSupervisorsAsync(
        IReadOnlyList<Claim> claims)
    {
        var since = DateTime.UtcNow.AddDays(-OverturnedWindowDays);

        var overturned = claims
            .Where(c => c.Status == ClaimStatus.REJECTED
                     && c.CurrentStep > 0
                     && c.UpdatedAt >= since)
            .ToList();

        if (overturned.Count == 0) return new();

        var directory = await _employees.GetSnapshotAsync();
        var tally = new Dictionary<string, OverturnedTally>(StringComparer.Ordinal);

        foreach (var claim in overturned)
        {
            var layerOne = await _router.CurrentApproversAsync(Module, claim.EmployeeId, 0);
            foreach (var approverId in layerOne)
            {
                if (!tally.TryGetValue(approverId, out var entry))
                    tally[approverId] = entry = new OverturnedTally();

                entry.ClaimIds.Add(claim.Id);
                entry.Employees.Add(claim.EmployeeId);
            }
        }

        return new OverturnedSupervisorsDto
        {
            Total = overturned.Count,
            Samples = tally
                .OrderByDescending(kv => kv.Value.ClaimIds.Count)
                .ThenBy(kv => Label(directory, kv.Key), StringComparer.OrdinalIgnoreCase)
                .Take(OverturnedSampleSize)
                .Select(kv => new OverturnedSupervisorDto
                {
                    SupervisorId = kv.Key,
                    SupervisorName = Label(directory, kv.Key),
                    OverturnedCount = kv.Value.ClaimIds.Count,
                    AffectedEmployees = kv.Value.Employees.Count,
                    ClaimIds = kv.Value.ClaimIds,
                })
                .ToList(),
        };
    }

    // Best available human label: the directory name, else the email (the UI
    // prettifies those), else the raw id so a row is never blank.
    private static string Label(EmployeeRowIndex directory, string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId)) return "Unassigned";

        var name = directory.NameOf(userId);
        if (!string.IsNullOrWhiteSpace(name)) return name;

        var email = directory.EmailOf(userId);
        return string.IsNullOrWhiteSpace(email) ? userId : email;
    }

    private sealed class OverturnedTally
    {
        public List<string> ClaimIds { get; } = [];
        public HashSet<string> Employees { get; } = new(StringComparer.Ordinal);
    }
}
