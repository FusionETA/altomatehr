using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Overtime;
using AltomateHR.Api.Modules.Overtime.Dtos;
using AltomateHR.Api.Modules.Overtime.Entities;
using AltomateHR.Api.Tests.Audit;
using AltomateHR.Api.Tests.Claims;
using AltomateHR.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Tests.Overtime;

// Several before- and after-work files per overtime request (photos or PDFs),
// up to ten a side. Before files are fixed once submitted; after files can be
// added and removed while the request is pending.
//
// Stored as a JSON list on the request, with the single-photo columns kept as
// the FIRST file of each side — so rows written before the lists existed (or
// by an older deployment on the same database) still read as one file.
public class OvertimeAttachmentsTests
{
    private const string Emp = "usr-emp";

    private static OvertimeAttachmentInputDto File(string name) =>
        new() { Url = $"/overtime/photos/{name}", FileName = name };

    private static CreateOvertimeRequestDto Submit(params OvertimeAttachmentInputDto[] before) => new()
    {
        WorkDate = new DateTime(2026, 9, 28),
        StartAt = new DateTime(2026, 9, 28, 2, 0, 0, DateTimeKind.Utc),
        EndAt = new DateTime(2026, 9, 28, 4, 0, 0, DateTimeKind.Utc),
        Reason = "Release night.",
        BeforeAttachments = [.. before],
    };

    private static (OvertimeService Service, FakeOvertimeRepository Repo, RecordingPhotoStorage Photos) Make(
        params OvertimeRequest[] requests)
    {
        var repo = new FakeOvertimeRepository(requests);
        var photos = new RecordingPhotoStorage();
        var service = new OvertimeService(
            repo, photos, new FakeSupervisionService(),
            // Someone above them, so attaching after files doesn't auto-approve.
            new FakeApprovalRouter(new() { [Emp] = [["usr-super"]] }),
            new FakeNotificationService(), new FakeTeamService(), new StubXeroReader());
        return (service, repo, photos);
    }

    private static OvertimeRequest Pending(string id = "ot-1") => new()
    {
        Id = id,
        OrganizationId = "org-1",
        EmployeeId = Emp,
        WorkDate = new DateTime(2026, 9, 28),
        StartAt = new DateTime(2026, 9, 28, 2, 0, 0, DateTimeKind.Utc),
        EndAt = new DateTime(2026, 9, 28, 4, 0, 0, DateTimeKind.Utc),
        RequestedMinutes = 120,
        Reason = "Release night.",
        BeforeAttachments = [new OvertimeAttachment { Url = "/overtime/photos/before.jpg", FileName = "before.jpg" }],
        AfterAttachments = [],
        Status = OvertimeStatus.PENDING,
        SubmittedAt = DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    // ─── Submitting ─────────────────────────────────────────────────────

    [Fact]
    public async Task Submit_KeepsEveryBeforeFile_AndMirrorsTheFirst()
    {
        var (service, _, _) = Make();

        var result = await service.SubmitAsync(
            Submit(File("site.jpg"), File("gate.png"), File("job-sheet.pdf")), Emp);

        Assert.True(result.Ok, result.Error);
        Assert.Equal(["site.jpg", "gate.png", "job-sheet.pdf"],
            result.Request!.BeforeAttachments.Select(a => a.FileName));
        Assert.Equal("/overtime/photos/site.jpg", result.Request.BeforePhotoUrl);
        Assert.Empty(result.Request.AfterAttachments);
    }

    [Fact]
    public async Task Submit_WithoutAnyBeforeFile_IsRefused()
    {
        var (service, _, _) = Make();

        var result = await service.SubmitAsync(Submit(), Emp);

        Assert.False(result.Ok);
        Assert.Contains("at least one before-work", result.Error);
    }

    [Fact]
    public async Task Submit_RefusesAFileThatWasNotUploadedHere()
    {
        var (service, _, _) = Make();

        var result = await service.SubmitAsync(
            Submit(File("ok.jpg"), new OvertimeAttachmentInputDto { Url = "https://evil.example/x.jpg" }), Emp);

        Assert.False(result.Ok);
        Assert.Contains("wasn't uploaded here", result.Error);
    }

    [Fact]
    public async Task Submit_RefusesMoreThanTenBeforeFiles()
    {
        var (service, _, _) = Make();

        var result = await service.SubmitAsync(
            Submit([.. Enumerable.Range(1, 11).Select(i => File($"p{i}.jpg"))]), Emp);

        Assert.False(result.Ok);
        Assert.Contains("up to 10", result.Error);
    }

    // What older clients and qa/ still send.
    [Fact]
    public async Task Submit_TheSinglePhotoFieldStillWorks()
    {
        var (service, _, _) = Make();
        var dto = Submit();
        dto.BeforePhotoUrl = "/overtime/photos/one.jpg";

        var result = await service.SubmitAsync(dto, Emp);

        Assert.True(result.Ok, result.Error);
        Assert.Equal("one.jpg", Assert.Single(result.Request!.BeforeAttachments).FileName);
    }

    // ─── After files ────────────────────────────────────────────────────

    [Fact]
    public async Task AttachAfter_AddsToWhatIsThere_AndSkipsADuplicate()
    {
        var request = Pending();
        var (service, _, _) = Make(request);

        await service.AttachAfterPhotoAsync(request.Id, Emp, new() { Attachments = [File("a.jpg"), File("b.jpg")] });
        var result = await service.AttachAfterPhotoAsync(
            request.Id, Emp, new() { Attachments = [File("b.jpg"), File("c.pdf")] });

        Assert.True(result.Transitioned, result.Error);
        Assert.Equal(["a.jpg", "b.jpg", "c.pdf"], request.AfterAttachments.Select(a => a.FileName));
        Assert.Equal("/overtime/photos/a.jpg", request.AfterPhotoUrl);   // the approval gate reads this
    }

    [Fact]
    public async Task AttachAfter_TheCapCountsWhatIsAlreadyThere()
    {
        var request = Pending();
        request.AfterAttachments = [.. Enumerable.Range(1, 9).Select(i => new OvertimeAttachment { Url = $"/overtime/photos/a{i}.jpg" })];
        var (service, _, _) = Make(request);

        var result = await service.AttachAfterPhotoAsync(
            request.Id, Emp, new() { Attachments = [File("x.jpg"), File("y.jpg")] });

        Assert.False(result.Transitioned);
        Assert.Contains("9 are already on this request", result.Error);
        Assert.Equal(9, request.AfterAttachments.Count);
    }

    [Fact]
    public async Task DeleteAttachment_RemovesOneAfterFile_AndItsLocalCopy()
    {
        var request = Pending();
        var (service, _, photos) = Make(request);
        await service.AttachAfterPhotoAsync(request.Id, Emp, new()
        {
            Attachments = [File("keep.jpg"), File("drop.jpg"),
                new OvertimeAttachmentInputDto { Url = "/overtime/photos/xero/xero-id-1", FileName = "in-xero.jpg" }],
        });
        var drop = request.AfterAttachments.Single(a => a.FileName == "drop.jpg");
        var xero = request.AfterAttachments.Single(a => a.FileName == "in-xero.jpg");

        await service.DeleteAttachmentAsync(request.Id, drop.Id, Emp);
        await service.DeleteAttachmentAsync(request.Id, xero.Id, Emp);

        Assert.Equal(["keep.jpg"], request.AfterAttachments.Select(a => a.FileName));
        // The local file is deleted; a Xero-hosted one is left in Xero Files
        // (the local store can't delete it — asking it to was a silent no-op).
        Assert.Equal(["drop.jpg"], photos.Deleted);
    }

    [Fact]
    public async Task DeleteAttachment_RefusesABeforeFile()
    {
        var request = Pending();
        var (service, _, photos) = Make(request);

        var result = await service.DeleteAttachmentAsync(request.Id, request.BeforeAttachments[0].Id, Emp);

        Assert.False(result.Transitioned);
        Assert.Contains("Before-work files can't be removed", result.Error);
        Assert.Single(request.BeforeAttachments);
        Assert.Empty(photos.Deleted);
    }

    [Fact]
    public async Task RemovingTheLastAfterFile_BlocksApprovalAgain()
    {
        var request = Pending();
        var (service, _, _) = Make(request);
        await service.AttachAfterPhotoAsync(request.Id, Emp, new() { Attachments = [File("only.jpg")] });

        await service.DeleteAttachmentAsync(request.Id, request.AfterAttachments[0].Id, Emp);
        var approve = await service.ApproveAsync(request.Id, "usr-super");

        Assert.Null(request.AfterPhotoUrl);
        Assert.False(approve.Transitioned);
        Assert.Contains("after-work photo or file", approve.Error);
    }

    [Fact]
    public async Task OnlyTheOwnerOfAPendingRequestCanChangeItsFiles()
    {
        var request = Pending();
        request.Status = OvertimeStatus.APPROVED;
        var (service, _, _) = Make(request);

        var notMine = await service.AttachAfterPhotoAsync(request.Id, "usr-other", new() { Attachments = [File("x.jpg")] });
        var decided = await service.AttachAfterPhotoAsync(request.Id, Emp, new() { Attachments = [File("x.jpg")] });

        Assert.False(notMine.Found);
        Assert.Contains("Only pending", decided.Error);
    }

    // ─── Rows from before the lists existed ─────────────────────────────

    [Fact]
    public async Task AnOlderRow_ReadsItsSinglePhotosAsOneFileEach_AndCanStillBeEdited()
    {
        var legacy = new OvertimeRequest
        {
            Id = "ot-old", EmployeeId = Emp, Status = OvertimeStatus.PENDING,
            BeforePhotoUrl = "/overtime/photos/old-before.jpg",
            AfterPhotoUrl = "/overtime/photos/old-after.jpg",
            // BeforeAttachmentsJson / AfterAttachmentsJson left null.
        };
        var (service, _, photos) = Make(legacy);

        Assert.Equal("old-before.jpg", Assert.Single(legacy.BeforeAttachments).FileName);
        var after = Assert.Single(legacy.AfterAttachments);

        var result = await service.DeleteAttachmentAsync(legacy.Id, after.Id, Emp);

        Assert.True(result.Transitioned, result.Error);
        Assert.Empty(legacy.AfterAttachments);
        Assert.Null(legacy.AfterPhotoUrl);
        Assert.Equal(["old-after.jpg"], photos.Deleted);
    }

    private sealed class RecordingPhotoStorage : IOvertimePhotoStorage
    {
        public List<string> Deleted { get; } = [];
        public Task<OvertimePhotoUploadResult> StoreAsync(OvertimePhotoUpload upload) => throw new NotSupportedException();
        public Task<OvertimePhotoFileResult?> GetAsync(string fileName) =>
            Task.FromResult<OvertimePhotoFileResult?>(new($"/tmp/{fileName}", "image/jpeg", fileName));
        public Task<bool> DeleteAsync(string fileName)
        {
            Deleted.Add(fileName);
            return Task.FromResult(true);
        }
    }
}

// Photo access starts by finding the request a file belongs to. The
// single-photo columns only hold the FIRST file of each side, so the lookup
// has to search the lists — or every second file 404s, as attendance's did.
public class OvertimePhotoLookupTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly OvertimeRepository _repo;

    public OvertimePhotoLookupTests()
    {
        _db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"ot-photos-{Guid.NewGuid()}").Options,
            new StubCurrentUser());
        _repo = new OvertimeRepository(_db);

        _db.OvertimeRequests.Add(new OvertimeRequest
        {
            Id = "ot-1",
            EmployeeId = "usr-emp",
            Reason = "x",
            BeforeAttachments =
            [
                new OvertimeAttachment { Url = "/overtime/photos/b1.jpg" },
                new OvertimeAttachment { Url = "/overtime/photos/b2.pdf" },
            ],
            AfterAttachments = [new OvertimeAttachment { Url = "/overtime/photos/a1.jpg" }],
        });
        _db.OvertimeRequests.Add(new OvertimeRequest
        {
            Id = "ot-old", EmployeeId = "usr-emp", Reason = "x",
            BeforePhotoUrl = "/overtime/photos/legacy.jpg",   // no lists: written before they existed
        });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    [Theory]
    [InlineData("/overtime/photos/b2.pdf", "ot-1")]     // a second before file
    [InlineData("/overtime/photos/a1.jpg", "ot-1")]
    [InlineData("/overtime/photos/legacy.jpg", "ot-old")]
    public async Task FindsTheRequestForAnyOfItsFiles(string url, string expected)
    {
        Assert.Equal(expected, (await _repo.GetByPhotoUrlAsync(url))?.Id);
    }

    // "Contains" narrows in SQL; the exact match is what decides.
    [Fact]
    public async Task APartialUrlMatchesNothing()
    {
        Assert.Null(await _repo.GetByPhotoUrlAsync("/overtime/photos/b"));
    }
}
