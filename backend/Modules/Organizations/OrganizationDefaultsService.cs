using AltomateHR.Api.Modules.Policies;
using AltomateHR.Api.Modules.Policies.Entities;
using AltomateHR.Api.Modules.Projects;
using AltomateHR.Api.Modules.Projects.Entities;
using AltomateHR.Api.Modules.Teams;
using AltomateHR.Api.Modules.Teams.Entities;
using AltomateHR.Api.Modules.Employees.Entities;

namespace AltomateHR.Api.Modules.Organizations;

// What a brand-new tenant needs before anyone can do anything in it.
//
// Leave types were already seeded at creation; a policy, a project and a team
// were not, so a new org arrived unusable in three separate ways: no policy
// means no module access and no OT rules, no project means nobody can clock
// in, and no team means no approval chain for anything they file. The admin
// had to find three different settings screens before the org did anything.
//
// Ported from the legacy seedDefaultsForNewOrganization, including its names —
// "<Org> Project (default)" and "<Org> Team (default)" — so an org created in
// either system looks the same.
public interface IOrganizationDefaultsService
{
    // Returns what it created, so a caller can report it. Idempotent: each
    // aggregate is checked independently, so an org that already has a policy
    // but no team gets only the team.
    Task<OrganizationDefaultsResult> EnsureForOrganizationAsync(
        string organizationId, string organizationName);
}

public readonly record struct OrganizationDefaultsResult(
    int PoliciesCreated, bool ProjectCreated, bool TeamCreated)
{
    public bool AnythingCreated => PoliciesCreated > 0 || ProjectCreated || TeamCreated;
}

public class OrganizationDefaultsService : IOrganizationDefaultsService
{
    private readonly IEmployeePolicyRepository _policies;
    private readonly IProjectRepository _projects;
    private readonly ITeamRepository _teams;

    public OrganizationDefaultsService(
        IEmployeePolicyRepository policies, IProjectRepository projects, ITeamRepository teams)
    {
        _policies = policies;
        _projects = projects;
        _teams = teams;
    }

    public async Task<OrganizationDefaultsResult> EnsureForOrganizationAsync(
        string organizationId, string organizationName)
    {
        var name = organizationName.Trim();
        if (name.Length == 0) name = "Organization";
        var now = DateTime.UtcNow;

        // Every write sets OrganizationId EXPLICITLY. The tenant stamp only
        // fills a BLANK one, and at creation time the caller's ambient org is
        // the OLD one — while a backfill runs with no ambient org at all.
        //
        // Reads are filtered by org for a request, and unfiltered when there is
        // no current org, so both callers have to narrow by hand.
        var existingPolicies = (await _policies.GetAllAsync())
            .Count(p => p.OrganizationId == organizationId);

        var policiesCreated = 0;
        if (existingPolicies == 0)
        {
            // Monthly FIRST: the first policy an org gets becomes its default,
            // and a monthly salary is the common case. Created second, hourly
            // workers would inherit a default that prorates them wrongly.
            await _policies.AddAsync(NewPolicy(organizationId, "Monthly Workers", SalaryType.MONTHLY, isDefault: true, now));
            await _policies.AddAsync(NewPolicy(organizationId, "Hourly Workers", SalaryType.HOURLY, isDefault: false, now));
            policiesCreated = 2;
        }

        var project = (await _projects.GetAllAsync())
            .FirstOrDefault(p => p.OrganizationId == organizationId);

        var projectCreated = false;
        if (project is null)
        {
            project = await _projects.AddAsync(new Project
            {
                OrganizationId = organizationId,
                Name = $"{name} Project (default)",
                CreatedAt = now,
            });
            projectCreated = true;
        }

        var hasTeam = (await _teams.GetAllAsync())
            .Any(t => t.OrganizationId == organizationId);

        var teamCreated = false;
        if (!hasTeam)
        {
            await _teams.AddAsync(new Team
            {
                OrganizationId = organizationId,
                ProjectId = project.Id,
                Name = $"{name} Team (default)",
                // One layer: everyone sits at layer 0 with nobody above them,
                // so requests auto-approve until an admin adds a supervisor
                // layer. Better than inventing a hierarchy nobody asked for.
                LayerCount = 1,
                CreatedAt = now,
                UpdatedAt = now,
            });
            teamCreated = true;
        }

        return new OrganizationDefaultsResult(policiesCreated, projectCreated, teamCreated);
    }

    // The legacy defaults, field for field: full module access, geofence on,
    // selfie off, OT enabled at the statutory multipliers.
    private static EmployeePolicy NewPolicy(
        string organizationId, string name, SalaryType salaryType, bool isDefault, DateTime now) => new()
    {
        OrganizationId = organizationId,
        Name = name,
        IsDefault = isDefault,
        SalaryType = salaryType,
        CanAccessAttendance = true,
        CanAccessClaims = true,
        CanAccessLeave = true,
        RequireGeofence = true,
        RequireSelfie = false,
        OtEnabled = true,
        OtRateNormalDay = 1.50m,
        OtRateRestDay = 2.00m,
        OtRatePublicHoliday = 3.00m,
        OtRateRestDayInShift = 1.00m,
        OtRatePublicHolidayInShift = 2.00m,
        CreatedAt = now,
        UpdatedAt = now,
    };
}
