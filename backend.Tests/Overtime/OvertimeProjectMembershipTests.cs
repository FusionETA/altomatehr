using AltomateHR.Api.Modules.Overtime;
using AltomateHR.Api.Modules.Overtime.Dtos;
using AltomateHR.Api.Modules.Overtime.Entities;
using AltomateHR.Api.Modules.Teams;
using AltomateHR.Api.Tests.Claims;   // reuse FakeSupervisionService, FakeApprovalRouter, FakeTeamService
using AltomateHR.Api.Tests.Support;

namespace AltomateHR.Api.Tests.Overtime;

// Overtime routes by (employee, project) exactly like a claim does, so a
// project the employee isn't on sends the request to a fallback team — right
// approvers, wrong costing. Claims and attendance already refused this;
// overtime was the last module that didn't.
public class OvertimeProjectMembershipTests
{
    [Fact]
    public async Task SubmitAsync_Rejects_AProjectTheEmployeeIsNotOn()
    {
        var service = Create(onProjects: ["prj-mine"]);

        var result = await service.SubmitAsync(Dto("prj-someone-elses"), "usr-emp");

        Assert.False(result.Ok);
        Assert.Contains("not assigned to that project", result.Error);
    }

    [Fact]
    public async Task SubmitAsync_Accepts_AProjectTheEmployeeIsOn()
    {
        var service = Create(onProjects: ["prj-mine"]);

        var result = await service.SubmitAsync(Dto("prj-mine"), "usr-emp");

        Assert.True(result.Ok);
        Assert.Equal("prj-mine", result.Request!.ProjectId);
    }

    [Fact]
    public async Task SubmitAsync_Requires_AProject_WhenTheEmployeeHasOne()
    {
        var service = Create(onProjects: ["prj-mine"]);

        var result = await service.SubmitAsync(Dto(null), "usr-emp");

        Assert.False(result.Ok);
        Assert.Contains("Pick the project", result.Error);
    }

    [Fact]
    public async Task SubmitAsync_Allows_NoProject_WhenTheEmployeeIsOnNone()
    {
        // Nothing to pick. Requiring one would lock an unassigned employee out
        // of claiming overtime at all, so the form hides the field for them.
        var service = Create(onProjects: []);

        var result = await service.SubmitAsync(Dto(null), "usr-emp");

        Assert.True(result.Ok);
        Assert.Null(result.Request!.ProjectId);
    }

    [Fact]
    public async Task SubmitAsync_Rejects_AnyProject_WhenTheEmployeeIsOnNone()
    {
        var service = Create(onProjects: []);

        var result = await service.SubmitAsync(Dto("prj-anything"), "usr-emp");

        Assert.False(result.Ok);
    }

    // ---- wiring ----

    private static OvertimeService Create(string[] onProjects) =>
        new(new FakeOvertimeRepository([]),
            new UnusedPhotoStorage(),
            new FakeSupervisionService(),
            new FakeApprovalRouter(new() { ["usr-emp"] = [["usr-super"]] }),
            new FakeNotificationService(),
            new FakeTeamService(projectsOf: new() { ["usr-emp"] = [.. onProjects] }),
            new StubXeroReader());

    private static CreateOvertimeRequestDto Dto(string? projectId) => new()
    {
        ProjectId = projectId,
        WorkDate = new DateTime(2026, 9, 15),
        StartAt = new DateTime(2026, 9, 15, 19, 0, 0),
        EndAt = new DateTime(2026, 9, 15, 21, 0, 0),
        Reason = "Deployment window",
        BeforePhotoUrl = "/overtime/photos/before.jpg",
    };
}

// Overtime now proxies Xero-hosted before/after photos. No test here exercises
// that, so the reader has no connection — which is also what an org without
// Xero looks like.
internal sealed class StubXeroReader : AltomateHR.Api.Modules.Xero.IXeroFileReader
{
    public Task<AltomateHR.Api.Modules.Xero.XeroFileContent?> GetFileContentAsync(string fileId) =>
        Task.FromResult<AltomateHR.Api.Modules.Xero.XeroFileContent?>(null);
}
