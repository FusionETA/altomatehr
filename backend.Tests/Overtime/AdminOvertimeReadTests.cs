using AltomateHR.Api.Modules.Overtime;
using AltomateHR.Api.Modules.Overtime.Dtos;
using AltomateHR.Api.Modules.Overtime.Entities;
using AltomateHR.Api.Modules.Teams;
using AltomateHR.Api.Tests.Claims;   // reuse FakeSupervisionService + FakeApprovalRouter
using AltomateHR.Api.Tests.Support;

namespace AltomateHR.Api.Tests.Overtime;

// The org-wide read behind the admin attendance view.
//
// It exists because the obvious alternative is wrong: GetTeamAsync is scoped to
// whoever may decide a request, and an admin is never in an approval chain, so
// reusing it would hand an admin an empty page and look like missing data. The
// first test here is that difference.
public class AdminOvertimeReadTests
{
    [Fact]
    public async Task GetAllForAdminAsync_ReturnsRequestsNoAdminCouldEverApprove()
    {
        // The router routes to a supervisor, never to the admin asking.
        var service = CreateService(
            [NewRequest("ot-a"), NewRequest("ot-b", employeeId: "usr-other")],
            new FakeApprovalRouter(new()
            {
                ["usr-emp"] = [["usr-super"]],
                ["usr-other"] = [["usr-super"]],
            }));

        var all = await service.GetAllForAdminAsync();

        Assert.Equal(2, all.Count());
    }

    [Fact]
    public async Task GetTeamAsync_ShowsAnAdminNothing_WhichIsWhyTheAdminReadExists()
    {
        // Pinning the premise rather than trusting it. If routing ever starts
        // including admins, this failing is the signal to reconsider the extra
        // read — not a reason to edit the assertion.
        var service = CreateService(
            [NewRequest("ot-a")],
            new FakeApprovalRouter(new() { ["usr-emp"] = [["usr-super"]] }));

        Assert.Empty(await service.GetTeamAsync("usr-admin"));
        Assert.Single(await service.GetAllForAdminAsync());
    }

    [Fact]
    public async Task GetAllForAdminAsync_PutsTheMostRecentWorkDateFirst()
    {
        // The view pages from the top, so "newest" has to come from the query
        // rather than from whatever order the rows happened to be stored in.
        var service = CreateService(
            [
                NewRequest("older", workDate: new DateTime(2026, 8, 1)),
                NewRequest("newest", workDate: new DateTime(2026, 9, 6)),
                NewRequest("middle", workDate: new DateTime(2026, 9, 1)),
            ],
            new FakeApprovalRouter(new() { ["usr-emp"] = [["usr-super"]] }));

        var all = (await service.GetAllForAdminAsync()).ToList();

        Assert.Equal(["newest", "middle", "older"], all.Select(r => r.Id));
    }

    [Fact]
    public async Task GetAllForAdminAsync_CarriesTheEmployeeEmailForEveryRow()
    {
        // Without it the table shows a GUID, and an admin cannot tell whose
        // overtime they are looking at.
        var service = CreateService(
            [NewRequest("ot-a")],
            new FakeApprovalRouter(new() { ["usr-emp"] = [["usr-super"]] }),
            emails: new() { ["usr-emp"] = "evan@altomate.com" });

        var row = Assert.Single(await service.GetAllForAdminAsync());

        Assert.Equal("evan@altomate.com", row.EmployeeEmail);
    }

    [Fact]
    public async Task GetAllForAdminAsync_ReturnsEveryStatusNotJustPending()
    {
        // The tab is a record, not a queue: a rejected request is exactly the
        // one an admin goes looking for.
        var service = CreateService(
            [
                NewRequest("p", status: OvertimeStatus.PENDING),
                NewRequest("a", status: OvertimeStatus.APPROVED),
                NewRequest("r", status: OvertimeStatus.REJECTED),
                NewRequest("c", status: OvertimeStatus.CANCELLED),
            ],
            new FakeApprovalRouter(new() { ["usr-emp"] = [["usr-super"]] }));

        var all = await service.GetAllForAdminAsync();

        Assert.Equal(4, all.Count());
    }

    [Fact]
    public async Task ApproveAsync_RecordsWhoDecided()
    {
        // The admin table names the reviewer, so the decision has to leave a
        // trace of who made it — DecidedAt alone says only that someone did.
        var request = NewRequest("ot-a");
        var service = CreateService(
            [request],
            new FakeApprovalRouter(new() { ["usr-emp"] = [["usr-super"]] }));

        await service.ApproveAsync("ot-a", "usr-super");

        Assert.Equal("usr-super", request.ReviewerId);
        Assert.Equal(OvertimeStatus.APPROVED, request.Status);
    }

    [Fact]
    public async Task RejectAsync_RecordsWhoDecided()
    {
        var request = NewRequest("ot-a");
        var service = CreateService(
            [request],
            new FakeApprovalRouter(new() { ["usr-emp"] = [["usr-super"]] }));

        await service.RejectAsync("ot-a", "usr-super", "Not approved for this project.");

        Assert.Equal("usr-super", request.ReviewerId);
        Assert.Equal(OvertimeStatus.REJECTED, request.Status);
    }

    [Fact]
    public async Task AutoApprovalWithNoApproverNamesNobody()
    {
        // An employee with nobody above them has their request completed when
        // the after-work photo lands — not at submit, because approval refuses
        // without that photo. No person reviewed it, and putting a name in the
        // reviewer column on an audit surface would be a fabrication.
        var request = NewRequest("ot-a", afterPhoto: null);
        var service = CreateService([request], new FakeApprovalRouter(new()));

        var result = await service.AttachAfterPhotoAsync(
            "ot-a", "usr-emp",
            new AttachOvertimeAfterPhotoDto { AfterPhotoUrl = "/overtime/photos/after.png" });

        Assert.True(result.Transitioned);
        Assert.Equal(OvertimeStatus.APPROVED, request.Status);
        Assert.Null(request.ReviewerId);
        // Decided, but by the rules rather than by a person — the pairing the
        // UI reads to show "Auto-approved" instead of a name.
        Assert.NotNull(request.DecidedAt);
    }

    [Fact]
    public async Task GetAllForAdminAsync_NamesTheReviewerNotJustTheirId()
    {
        var request = NewRequest("ot-a", status: OvertimeStatus.APPROVED);
        request.ReviewerId = "usr-super";
        var service = CreateService(
            [request],
            new FakeApprovalRouter(new() { ["usr-emp"] = [["usr-super"]] }),
            emails: new() { ["usr-emp"] = "evan@x.com", ["usr-super"] = "sara@x.com" });

        var row = Assert.Single(await service.GetAllForAdminAsync());

        Assert.Equal("usr-super", row.ReviewerId);
        Assert.Equal("sara@x.com", row.ReviewerEmail);
    }

    private static OvertimeService CreateService(
        IEnumerable<OvertimeRequest> requests,
        IApprovalRouter router,
        Dictionary<string, string>? emails = null) =>
        new(new FakeOvertimeRepository(requests),
            new UnusedPhotoStorage(),
            new FakeSupervisionService(emails: emails),
            router,
            new FakeNotificationService());

    private static OvertimeRequest NewRequest(
        string id,
        string employeeId = "usr-emp",
        OvertimeStatus status = OvertimeStatus.PENDING,
        DateTime? workDate = null,
        string? afterPhoto = "/overtime/photos/after.png") => new()
        {
            Id = id,
            OrganizationId = "org-1",
            EmployeeId = employeeId,
            WorkDate = workDate ?? new DateTime(2026, 9, 1),
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
