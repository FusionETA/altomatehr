using AltomateHR.Api.Modules.Claims;
using AltomateHR.Api.Modules.Claims.Dtos;
using AltomateHR.Api.Modules.Claims.Entities;
using AltomateHR.Api.Modules.Teams;
using static AltomateHR.Api.Tests.Claims.ClaimsTestFactory;

namespace AltomateHR.Api.Tests.Claims;

// A claim's project decides which team approves it: the router looks the chain
// up by (employee, project). Pick a project you're not on and the lookup falls
// back to a team you ARE on — plausible approvers, wrong costing — so the
// server has to refuse it, not just the form.
public class ClaimsProjectMembershipTests
{
    private static ITeamService TeamsWithProjects(params string[] projectIds) =>
        new FakeTeamService(projectsOf: new() { ["usr-emp"] = [.. projectIds] });

    [Fact]
    public async Task CreateAsync_Rejects_AProjectTheEmployeeIsNotOn()
    {
        var service = CreateService([], teams: TeamsWithProjects("prj-mine"));

        var error = await Assert.ThrowsAsync<ClaimValidationException>(
            () => service.CreateAsync(DtoForProject("prj-someone-elses"), "usr-emp"));

        Assert.Equal(nameof(CreateClaimDto.ProjectId), error.Field);
    }

    [Fact]
    public async Task CreateAsync_Accepts_AProjectTheEmployeeIsOn()
    {
        var service = CreateService([], teams: TeamsWithProjects("prj-mine"));

        var claim = await service.CreateAsync(DtoForProject("prj-mine"), "usr-emp");

        Assert.Equal("prj-mine", claim.ProjectId);
    }

    [Fact]
    public async Task CreateAsync_Rejects_AnyProject_WhenTheEmployeeIsOnNone()
    {
        // The form hides the field entirely for these people; this pins that a
        // hand-rolled request can't route around that.
        var service = CreateService([], teams: TeamsWithProjects());

        await Assert.ThrowsAsync<ClaimValidationException>(
            () => service.CreateAsync(DtoForProject("prj-mine"), "usr-emp"));
    }

    [Fact]
    public async Task CreateAsync_Requires_AProject_WhenTheEmployeeHasOne()
    {
        // The project decides the approval route and the costing, so anyone
        // with one to pick has to pick it.
        var service = CreateService([], teams: TeamsWithProjects("prj-mine"));

        var error = await Assert.ThrowsAsync<ClaimValidationException>(
            () => service.CreateAsync(DtoForProject(null), "usr-emp"));

        Assert.Equal(nameof(CreateClaimDto.ProjectId), error.Field);
    }

    [Fact]
    public async Task CreateAsync_Allows_NoProject_WhenTheEmployeeIsOnNone()
    {
        // Nothing to pick. Requiring one here would lock an unassigned employee
        // out of claiming altogether, so the form hides the field for them.
        var service = CreateService([], teams: TeamsWithProjects());

        var claim = await service.CreateAsync(DtoForProject(null), "usr-emp");

        Assert.Null(claim.ProjectId);
    }

    private static CreateClaimDto DtoForProject(string? projectId) => new()
    {
        Title = "Taxi to site",
        Description = "Client meeting",
        Category = ClaimCategory.TRANSPORT,
        Amount = 25.00m,
        SpentAt = DateTime.UtcNow,
        ClaimType = ClaimType.EXPENSE,
        PaymentType = PaymentType.PERSONAL,
        ChartOfAccountId = "acct-expense",
        ProjectId = projectId,
    };
}
