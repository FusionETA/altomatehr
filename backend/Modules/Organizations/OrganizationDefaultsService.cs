using AltomateHR.Api.Modules.Policies;
using AltomateHR.Api.Modules.Policies.Entities;
using AltomateHR.Api.Modules.Projects;
using AltomateHR.Api.Modules.Projects.Entities;
using AltomateHR.Api.Modules.Teams;
using AltomateHR.Api.Modules.Teams.Entities;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Accounts;

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
    int PoliciesCreated, bool ProjectCreated, bool TeamCreated, int AccountsCreated = 0)
{
    public bool AnythingCreated => PoliciesCreated > 0 || ProjectCreated || TeamCreated || AccountsCreated > 0;
}

public class OrganizationDefaultsService : IOrganizationDefaultsService
{
    private readonly IEmployeePolicyRepository _policies;
    private readonly IProjectRepository _projects;
    private readonly ITeamRepository _teams;
    // Optional so hand-built instances in tests need not supply it.
    private readonly IChartOfAccountRepository? _accounts;
    private readonly Overtime.IOvertimeTypeRepository? _overtimeTypes;

    public OrganizationDefaultsService(
        IEmployeePolicyRepository policies, IProjectRepository projects, ITeamRepository teams,
        IChartOfAccountRepository? accounts = null, Overtime.IOvertimeTypeRepository? overtimeTypes = null)
    {
        _accounts = accounts;
        _overtimeTypes = overtimeTypes;
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

        // Claim item types, so claims work before anyone sets up accounts. A
        // new company has no Xero yet; one that has any account already keeps
        // its own list.
        var accountsCreated = 0;
        if (_accounts is not null
            && !(await _accounts.GetAllAsync()).Any(a => a.OrganizationId == organizationId))
        {
            foreach (var item in DefaultClaimItemTypes.All)
            {
                await _accounts.AddAsync(DefaultClaimItemTypes.ToAccount(item, organizationId, now));
                accountsCreated++;
            }
        }

        // The three built-in overtime types the policies' rates fall back to.
        if (_overtimeTypes is not null
            && !(await _overtimeTypes.GetAllAsync()).Any(t => t.OrganizationId == organizationId))
        {
            await _overtimeTypes.AddRangeAsync(Overtime.OvertimeTypeService.BuiltIns.Select(b => new Overtime.Entities.OvertimeType
            {
                OrganizationId = organizationId,
                Name = b.Name,
                Kind = b.Kind,
                Multiplier = b.Multiplier,
                CreatedAt = now,
                UpdatedAt = now,
            }));
        }

        return new OrganizationDefaultsResult(policiesCreated, projectCreated, teamCreated, accountsCreated);
    }

    // The legacy defaults: full module access, geofence on, selfie off, OT
    // enabled at the company's rates (the statutory multipliers to start).
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
        // OT rates left to the company's overtime types (1.5× / 2× / 3×).
        OtEnabled = true,
        CreatedAt = now,
        UpdatedAt = now,
    };
}
