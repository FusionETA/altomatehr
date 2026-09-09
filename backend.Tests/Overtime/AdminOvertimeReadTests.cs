using AltomateHR.Api.Modules.Overtime;
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
        DateTime? workDate = null) => new()
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
            AfterPhotoUrl = "/overtime/photos/after.png",
            Status = status,
            SubmittedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
}
