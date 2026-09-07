using AltomateHR.Api.Modules.Overtime;
using AltomateHR.Api.Modules.Overtime.Dtos;
using AltomateHR.Api.Modules.Overtime.Entities;
using AltomateHR.Api.Modules.Teams;
using AltomateHR.Api.Tests.Claims;

namespace AltomateHR.Api.Tests.Teams;

// What happens when there is nobody above you.
//
// Admins are oversight and take no part in the hierarchy (see OrgRoles), so the
// person at the top of a team has ZERO approval steps. Before this rule, their
// submissions were created PENDING anyway: invisible in every approval queue,
// rejected for every caller who tried to decide them, and therefore stuck
// forever. There is no one left to ask, so submitting IS the decision.
//
// The attendance, leave and claims halves of this live with their own modules'
// test doubles; this file covers overtime, whose timing is different.
public class NoApproverAboveTests
{
    private const string PhotoUrl = "/overtime/photos/after.jpg";

    [Fact]
    public async Task Overtime_StaysPendingAtSubmit_BecauseTheAfterPhotoIsMissing()
    {
        // Overtime can't be decided at submit like the others: approval refuses
        // without an after-work photo, and there isn't one yet. Auto-approving
        // here would smuggle a request past that rule.
        var repo = new StubOvertimeRepository();
        var service = Build(repo, approvers: []);

        var result = await service.SubmitAsync(NewOvertime(), "boss");

        Assert.True(result.Ok);
        Assert.Equal(OvertimeStatus.PENDING, repo.Saved.Single().Status);
    }

    [Fact]
    public async Task Overtime_IsApprovedWhenTheAfterPhotoLands_IfNobodyIsAbove()
    {
        var repo = new StubOvertimeRepository();
        var service = Build(repo, approvers: []);
        await service.SubmitAsync(NewOvertime(), "boss");
        var request = repo.Saved.Single();

        var result = await service.AttachAfterPhotoAsync(
            request.Id, "boss", new AttachOvertimeAfterPhotoDto { AfterPhotoUrl = PhotoUrl });

        Assert.True(result.Transitioned);
        Assert.Equal(OvertimeStatus.APPROVED, request.Status);
        Assert.NotNull(request.DecidedAt);
    }

    [Fact]
    public async Task Overtime_StillWaitsForAnApproverWhenOneExists()
    {
        // The guard that keeps the rule narrow: attaching the photo must not
        // decide a request that a real approver is supposed to review.
        var repo = new StubOvertimeRepository();
        var service = Build(repo, approvers: ["lead"]);
        await service.SubmitAsync(NewOvertime(), "staff");
        var request = repo.Saved.Single();

        await service.AttachAfterPhotoAsync(
            request.Id, "staff", new AttachOvertimeAfterPhotoDto { AfterPhotoUrl = PhotoUrl });

        Assert.Equal(OvertimeStatus.PENDING, request.Status);
        Assert.Null(request.DecidedAt);
    }

    private static CreateOvertimeRequestDto NewOvertime() => new()
    {
        WorkDate = new DateTime(2026, 9, 1),
        StartAt = new DateTime(2026, 9, 1, 18, 0, 0),
        EndAt = new DateTime(2026, 9, 1, 20, 0, 0),
        Reason = "Site handover ran late.",
        BeforePhotoUrl = "/overtime/photos/before.jpg",
    };

    private static OvertimeService Build(StubOvertimeRepository repo, string[] approvers)
    {
        var chains = new Dictionary<string, List<List<string>>>();
        if (approvers.Length > 0)
        {
            chains["staff"] = [[.. approvers]];
            chains["boss"] = [[.. approvers]];
        }
        return new OvertimeService(
            repo,
            new StubOvertimePhotos(),
            new FakeSupervisionService(),
            new FakeApprovalRouter(chains));
    }

    private sealed class StubOvertimeRepository : IOvertimeRepository
    {
        public List<OvertimeRequest> Saved { get; } = [];

        public Task<OvertimeRequest> AddAsync(OvertimeRequest request)
        {
            Saved.Add(request);
            return Task.FromResult(request);
        }

        public Task<OvertimeRequest?> GetByIdAsync(string id) =>
            Task.FromResult(Saved.FirstOrDefault(r => r.Id == id));

        public Task<List<OvertimeRequest>> GetAllAsync() => Task.FromResult(Saved);

        public Task<List<OvertimeRequest>> GetByEmployeeAsync(string employeeId) =>
            Task.FromResult(Saved.Where(r => r.EmployeeId == employeeId).ToList());

        public Task<OvertimeRequest?> GetByPhotoUrlAsync(string photoUrl) =>
            Task.FromResult<OvertimeRequest?>(null);

        public Task UpdateAsync(OvertimeRequest request) => Task.CompletedTask;
    }

    private sealed class StubOvertimePhotos : IOvertimePhotoStorage
    {
        public Task<OvertimePhotoUploadResult> StoreAsync(OvertimePhotoUpload upload) =>
            throw new NotSupportedException();
        public Task<OvertimePhotoFileResult?> GetAsync(string fileName) =>
            Task.FromResult<OvertimePhotoFileResult?>(null);
        public Task<bool> DeleteAsync(string fileName) => Task.FromResult(true);
    }
}
