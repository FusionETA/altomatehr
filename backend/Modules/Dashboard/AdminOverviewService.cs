using AltomateHR.Api.Modules.Claims;
using AltomateHR.Api.Modules.Claims.Entities;
using AltomateHR.Api.Modules.Dashboard.Dtos;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Projects;

namespace AltomateHR.Api.Modules.Dashboard;

// Computes the admin executive overview by aggregating org-wide data server-side (the
// repos are tenant-filtered, so this is scoped to the active org). Each card is gated by
// the caller's enabled modules — no claims module ⇒ no claims analytics, etc. Cards are
// added one at a time; only the ones implemented below return data.
public class AdminOverviewService : IAdminOverviewService
{
    private readonly IClaimsService _claims;
    private readonly IProjectService _projects;
    private readonly IModuleAccessService _modules;

    public AdminOverviewService(
        IClaimsService claims, IProjectService projects, IModuleAccessService modules)
    {
        _claims = claims;
        _projects = projects;
        _modules = modules;
    }

    public async Task<AdminOverviewDto> GetAsync()
    {
        // The caller's effective modules = org package ∩ their admin grant.
        var enabled = await _modules.GetEnabledModulesAsync();
        var has = new HashSet<string>(enabled, StringComparer.OrdinalIgnoreCase);

        return new AdminOverviewDto
        {
            EnabledModules = enabled.ToList(),
            ProjectSpend = has.Contains(OrgModules.Claims)
                ? await ProjectSpendThisMonthAsync()
                : new(),
            // AttendanceHealth (attendance) / SlowOtApprovers (overtime) / StalePendingClaims
            // + UpcomingClaimRun (claims) / OverturnedSupervisors are built one by one next.
        };
    }

    // Card 1 — claim spend grouped by project for the current month (excludes rejected).
    private async Task<List<ProjectClaimSpendDto>> ProjectSpendThisMonthAsync()
    {
        var now = DateTime.UtcNow;
        var claims = await _claims.GetAllForOrgAsync();
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
}
