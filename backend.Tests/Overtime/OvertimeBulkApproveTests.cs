using AltomateHR.Api.Modules.Overtime;
using AltomateHR.Api.Modules.Overtime.Entities;
using AltomateHR.Api.Modules.Teams;
using AltomateHR.Api.Tests.Claims;   // reuse FakeSupervisionService + FakeApprovalRouter
using AltomateHR.Api.Tests.Support;

namespace AltomateHR.Api.Tests.Overtime;

// Mirrors ClaimsBulkApproveTests: what bulk REFUSES matters more than what it
// approves. Overtime adds one refusal of its own — the after-work photo, which
// the single-approve path also gates on.
public class OvertimeBulkApproveTests
{
    private static FakeApprovalRouter SingleApprover() =>
        new(new() { ["usr-emp"] = [["usr-super"]] });

    [Fact]
    public async Task BulkApproveAsync_ApprovesEveryRequestTheCallerMayDecide()
    {
        var a = NewRequest("ot-a");
        var b = NewRequest("ot-b");
        var service = CreateService([a, b], SingleApprover());

        var result = await service.BulkApproveAsync(["ot-a", "ot-b"], "usr-super");

        Assert.Equal(2, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.Equal(OvertimeStatus.APPROVED, a.Status);
        Assert.Equal(OvertimeStatus.APPROVED, b.Status);
    }

    [Fact]
    public async Task BulkApproveAsync_RefusesARequestWithNoAfterPhotoAndSaysWhy()
    {
        var withPhoto = NewRequest("ot-a");
        var noPhoto = NewRequest("ot-b", afterPhoto: null);
        var service = CreateService([withPhoto, noPhoto], SingleApprover());

        var result = await service.BulkApproveAsync(["ot-a", "ot-b"], "usr-super");

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(1, result.Failed);
        Assert.Equal(OvertimeStatus.PENDING, noPhoto.Status);

        // The reason is the point: silently dropping it leaves the approver
        // wondering why the row is still in their queue.
        var failure = result.Items.Single(i => i.Id == "ot-b");
        Assert.Contains("after-work photo", failure.Error);
    }

    [Fact]
    public async Task BulkApproveAsync_FailsOnlyTheRequestsTheCallerCannotDecide()
    {
        var mine = NewRequest("mine");
        var other = NewRequest("other", employeeId: "usr-other");
        var service = CreateService([mine, other], new FakeApprovalRouter(new()
        {
            ["usr-emp"] = [["usr-super"]],
            ["usr-other"] = [["usr-someone-else"]],
        }));

        var result = await service.BulkApproveAsync(["mine", "other"], "usr-super");

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(1, result.Failed);
        Assert.Equal(OvertimeStatus.APPROVED, mine.Status);
        Assert.Equal(OvertimeStatus.PENDING, other.Status);
    }

    [Fact]
    public async Task BulkApproveAsync_SkipsRequestsThatWereAlreadyDecided()
    {
        var decided = NewRequest("done", status: OvertimeStatus.REJECTED);
        var service = CreateService([decided], SingleApprover());

        var result = await service.BulkApproveAsync(["done"], "usr-super");

        Assert.Equal(0, result.Succeeded);
        Assert.Equal(1, result.Failed);
        Assert.Equal(OvertimeStatus.REJECTED, decided.Status);
    }

    [Fact]
    public async Task BulkApproveAsync_CountsARepeatedIdOnce()
    {
        var request = NewRequest("ot-a");
        var service = CreateService([request], SingleApprover());

        var result = await service.BulkApproveAsync(["ot-a", "ot-a"], "usr-super");

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.Single(result.Items);
    }

    [Fact]
    public async Task BulkApproveAsync_RefusesTheWholeBatchWhenItIsTooLarge()
    {
        var service = CreateService([], SingleApprover());
        var ids = Enumerable.Range(0, 201).Select(i => $"ot-{i}").ToList();

        var result = await service.BulkApproveAsync(ids, "usr-super");

        Assert.Equal(0, result.Succeeded);
        Assert.Equal(201, result.Failed);
        Assert.Contains("Too many", result.Items.Single().Error);
    }

    [Fact]
    public async Task BulkApproveAsync_AdvancesTheChainInsteadOfApprovingOnAMultiStepChain()
    {
        var request = NewRequest("ot-a");
        var service = CreateService([request],
            new FakeApprovalRouter(new() { ["usr-emp"] = [["usr-super"], ["usr-boss"]] }));

        var result = await service.BulkApproveAsync(["ot-a"], "usr-super");

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(OvertimeStatus.PENDING, request.Status);   // still waiting on usr-boss
        Assert.Equal(1, request.CurrentStep);
    }

    // --- helpers ---

    private static OvertimeService CreateService(
        IEnumerable<OvertimeRequest> requests, IApprovalRouter router) =>
        new(new FakeOvertimeRepository(requests),
            new UnusedPhotoStorage(),
            new FakeSupervisionService(),
            router,
            new FakeNotificationService());

    private static OvertimeRequest NewRequest(
        string id,
        string employeeId = "usr-emp",
        OvertimeStatus status = OvertimeStatus.PENDING,
        string? afterPhoto = "/overtime/photos/after.png") => new()
        {
            Id = id,
            OrganizationId = "org-1",
            EmployeeId = employeeId,
            WorkDate = new DateTime(2026, 9, 1),
            StartAt = new DateTime(2026, 9, 1, 19, 0, 0, DateTimeKind.Utc),
            EndAt = new DateTime(2026, 9, 1, 21, 0, 0, DateTimeKind.Utc),
            RequestedMinutes = 120,
            Reason = "Release night.",
            BeforePhotoUrl = "/overtime/photos/before.png",
            AfterPhotoUrl = afterPhoto,
            Status = status,
            SubmittedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
}
