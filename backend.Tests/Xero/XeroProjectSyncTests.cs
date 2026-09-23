using AltomateHR.Api.Modules.Projects.Entities;
using AltomateHR.Api.Modules.Xero;
using AltomateHR.Api.Modules.Xero.Dtos;
using AltomateHR.Api.Modules.Xero.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

using AltomateHR.Api.Tests.Audit;

namespace AltomateHR.Api.Tests.Xero;

// Connecting Xero makes it the source of truth for which projects exist. The
// ones an admin typed in beforehand have to drop out of the pickers, or
// attendance and company structure keep offering projects that no claim, bill
// or timesheet can ever be costed against.
public class XeroProjectSyncTests
{
    [Fact]
    public async Task SyncProjectsAsync_ArchivesTheProjectsThatWereTypedInByHand()
    {
        var repo = new FakeXeroRepository();
        repo.Projects.Add(Manual("Mobile App"));
        repo.Projects.Add(Manual("Website Revamp"));

        var service = Create(repo, [new XeroProjectResponse("x-1", "Client Site A", "INPROGRESS")]);

        await service.SyncProjectsAsync();

        var manual = repo.Projects.Where(p => p.XeroProjectId is null).ToList();
        Assert.All(manual, p => Assert.True(p.IsArchived));
        Assert.All(manual, p => Assert.True(p.ArchivedByXeroConnect));
        // The one Xero supplied is untouched by the rule.
        Assert.False(repo.Projects.Single(p => p.XeroProjectId == "x-1").IsArchived);
    }

    [Fact]
    public async Task SyncProjectsAsync_LeavesManualProjectsAloneWhenXeroSuppliedNone()
    {
        // A Xero org with no projects — or one whose projects live in a
        // tracking category this sync can't see. Archiving here would leave
        // nothing selectable and nobody able to clock in.
        var repo = new FakeXeroRepository();
        repo.Projects.Add(Manual("Mobile App"));

        var service = Create(repo, []);

        await service.SyncProjectsAsync();

        Assert.False(repo.Projects.Single().IsArchived);
    }

    [Fact]
    public async Task SyncProjectsAsync_DoesNotClaimAProjectTheAdminArchivedThemselves()
    {
        // Already archived, so the flag must stay off — otherwise disconnecting
        // Xero would silently bring back something the admin retired on purpose.
        var repo = new FakeXeroRepository();
        var retired = Manual("Old Site");
        retired.IsArchived = true;
        repo.Projects.Add(retired);

        var service = Create(repo, [new XeroProjectResponse("x-1", "Client Site A", "INPROGRESS")]);

        await service.SyncProjectsAsync();

        Assert.True(retired.IsArchived);
        Assert.False(retired.ArchivedByXeroConnect);
    }

    [Fact]
    public async Task DisconnectAsync_BringsBackOnlyTheProjectsXeroHid()
    {
        var repo = new FakeXeroRepository();
        var hidden = Manual("Mobile App");
        var retired = Manual("Old Site");
        retired.IsArchived = true;
        repo.Projects.Add(hidden);
        repo.Projects.Add(retired);

        var service = Create(repo, [new XeroProjectResponse("x-1", "Client Site A", "INPROGRESS")]);
        await service.SyncProjectsAsync();
        Assert.True(hidden.IsArchived);

        await service.DisconnectAsync();

        Assert.False(hidden.IsArchived);
        Assert.False(hidden.ArchivedByXeroConnect);
        // Never Xero's to restore.
        Assert.True(retired.IsArchived);
    }

    // ---- wiring ----

    private static Project Manual(string name) => new()
    {
        OrganizationId = "org-1",
        Name = name,
        XeroProjectId = null,
    };

    private static XeroService Create(FakeXeroRepository repo, List<XeroProjectResponse> projects)
    {
        repo.Connection = new XeroConnection
        {
            OrganizationId = "org-1",
            TenantId = "tenant-1",
            AccessTokenProtected = Protect("token"),
            RefreshTokenProtected = Protect("refresh"),
            AccessTokenExpiresAt = DateTime.UtcNow.AddHours(1),
        };

        return new XeroService(
            new FakeXeroCurrentUser(),
            repo,
            new FakeXeroProjectsClient(projects),
            Provider,
            Options.Create(new XeroOptions()),
            new FakeAuditService());
    }

    private static readonly IDataProtectionProvider Provider =
        DataProtectionProvider.Create("AltomateHR.Tests");

    private static string Protect(string value) =>
        Provider.CreateProtector("AltomateHR.XeroTokens.v1").Protect(value);
}

internal sealed class FakeXeroProjectsClient : IXeroClient
{
    private readonly List<XeroProjectResponse> _projects;
    private readonly List<XeroTrackingCategoryResponse> _categories;

    public FakeXeroProjectsClient(
        List<XeroProjectResponse> projects,
        List<XeroTrackingCategoryResponse>? categories = null)
    {
        _projects = projects;
        _categories = categories ?? [];
    }

    public Task<List<XeroProjectResponse>> GetProjectsAsync(string a, string t) =>
        Task.FromResult(_projects);

    public Task<List<XeroCurrencyResponse>> GetCurrenciesAsync(string a, string t) =>
        Task.FromResult(new List<XeroCurrencyResponse>());
    public Task<List<XeroAccountResponse>> GetAccountsAsync(string a, string t) =>
        Task.FromResult(new List<XeroAccountResponse>());
    public string BuildAuthorizationUrl(string state) => throw new NotSupportedException();
    public Task<XeroTokenResponse> ExchangeCodeAsync(string code) => throw new NotSupportedException();
    public Task<XeroTokenResponse> RefreshTokenAsync(string r) => throw new NotSupportedException();
    public Task<List<XeroTenantResponse>> GetTenantsAsync(string a) => throw new NotSupportedException();
    public Task<XeroFileContent?> GetFileContentAsync(string a, string t, string f) => throw new NotSupportedException();
    public Task<XeroBillResponse> CreateBillAsync(string a, string t, XeroBillRequest b) => throw new NotSupportedException();
    public Task<XeroSpendResponse> CreateSpendAsync(string a, string t, XeroSpendRequest s) => throw new NotSupportedException();
    public Task<XeroManualJournalResponse> CreateManualJournalAsync(
        string a, string t, XeroManualJournalRequest j) => throw new NotSupportedException();
    public Task<List<XeroTrackingCategoryResponse>> GetTrackingCategoriesAsync(string a, string t) =>
        Task.FromResult(_categories);

    // Files API: nothing under test here uploads.
    public Task<string?> EnsureFolderAsync(string accessToken, string tenantId, string folderName) =>
        Task.FromResult<string?>(null);

    public Task<XeroUploadedFile> UploadFileAsync(
        string accessToken, string tenantId, string? folderId,
        byte[] content, string fileName, string contentType) =>
        throw new NotSupportedException();
}

// The frontend has no router, so the callback's redirect is the only thing that
// can get the admin back to the Xero card and tell them what happened. The
// marker is that signal — XeroConnectionCard reads it and draws the banner.
public class XeroCallbackRedirectTests
{
    [Theory]
    [InlineData("", "state-1")]
    [InlineData("code-1", "")]
    public async Task CompleteCallbackAsync_MarksTheRedirectFailed_WhenXeroSentNothingUsable(
        string code, string state)
    {
        var url = await Create().CompleteCallbackAsync(code, state);

        Assert.EndsWith("?xero=failed", url);
    }

    [Fact]
    public async Task CompleteCallbackAsync_MarksTheRedirectFailed_WhenTheStateIsUnknown()
    {
        // An expired, replayed or forged state — nothing to exchange, and the
        // admin still has to be told rather than silently dropped somewhere.
        var url = await Create().CompleteCallbackAsync("code-1", "state-nobody-issued");

        Assert.EndsWith("?xero=failed", url);
    }

    private static XeroService Create() => new(
        new FakeXeroCurrentUser(),
        new FakeXeroRepository(),
        new FakeXeroProjectsClient([]),
        DataProtectionProvider.Create("AltomateHR.Tests"),
        Options.Create(new XeroOptions { FailureRedirectUrl = "http://localhost:5173/" }),
        new FakeAuditService());
}

// Most Xero orgs don't use the Projects product. They model projects as options
// on a tracking category — which is what the previous system syncs, and what
// the Projects API can never see. Syncing only the latter reported a truthful
// "0 added, 0 updated" against a Xero that plainly had projects in it.
public class XeroTrackingCategoryProjectSyncTests
{
    [Fact]
    public async Task SyncProjectsAsync_ImportsEachOptionOfTheOnlyTrackingCategory()
    {
        var repo = new FakeXeroRepository();
        var service = Create(repo, [Category("cat-1", "Altomatehr", ("opt-1", "Office"), ("opt-2", "ZR"))]);

        var result = await service.SyncProjectsAsync();

        Assert.Equal(2, result.Imported);
        Assert.Equal("Altomatehr", result.TrackingCategoryName);
        Assert.Equal(["Office", "ZR"], repo.Projects.Select(p => p.Name));
        Assert.All(repo.Projects, p => Assert.Equal("cat-1", p.XeroTrackingCategoryId));
        // The Projects API is a different Xero product; these rows aren't from it.
        Assert.All(repo.Projects, p => Assert.Null(p.XeroProjectId));
    }

    [Fact]
    public async Task SyncProjectsAsync_RenamesInPlaceRatherThanImportingTwice()
    {
        var repo = new FakeXeroRepository();
        repo.Projects.Add(new Project
        {
            OrganizationId = "org-1",
            Name = "Office",
            XeroTrackingOptionId = "opt-1",
        });

        var service = Create(repo, [Category("cat-1", "Altomatehr", ("opt-1", "Head Office"))]);
        var result = await service.SyncProjectsAsync();

        Assert.Equal(0, result.Imported);
        Assert.Equal(1, result.Updated);
        Assert.Equal("Head Office", repo.Projects.Single().Name);
    }

    [Fact]
    public async Task SyncProjectsAsync_AsksWhichCategory_WhenXeroOffersMoreThanOne()
    {
        // Picking for them would fill the project list with regions or cost
        // centres. "0 added" with nothing to act on was the old answer.
        var repo = new FakeXeroRepository();
        var service = Create(repo, [
            Category("cat-1", "Altomatehr", ("opt-1", "Office")),
            Category("cat-2", "Region", ("opt-9", "North")),
        ]);

        var result = await service.SyncProjectsAsync();

        Assert.True(result.NeedsTrackingCategoryChoice);
        Assert.Empty(repo.Projects);
    }

    [Fact]
    public async Task SyncProjectsAsync_UsesTheChosenCategory_WhenOneIsPicked()
    {
        var repo = new FakeXeroRepository();
        var service = Create(repo, [
            Category("cat-1", "Altomatehr", ("opt-1", "Office")),
            Category("cat-2", "Region", ("opt-9", "North")),
        ]);
        repo.Connection!.ProjectTrackingCategoryId = "cat-2";

        var result = await service.SyncProjectsAsync();

        Assert.False(result.NeedsTrackingCategoryChoice);
        Assert.Equal("North", repo.Projects.Single().Name);
    }

    [Fact]
    public async Task SyncProjectsAsync_ArchivesAnOptionXeroHasArchived()
    {
        var repo = new FakeXeroRepository();
        var service = Create(repo, [new XeroTrackingCategoryResponse(
            "cat-1", "Altomatehr", "ACTIVE",
            [new XeroTrackingOptionResponse("opt-1", "Old Site", "ARCHIVED")])]);

        await service.SyncProjectsAsync();

        Assert.True(repo.Projects.Single().IsArchived);
    }

    [Fact]
    public async Task SyncProjectsAsync_DoesNotTreatATrackingProjectAsHandCreated()
    {
        // These have no XeroProjectId either, so the archive-the-manual-ones
        // rule would otherwise hide every project it had just imported.
        var repo = new FakeXeroRepository();
        var service = Create(repo, [Category("cat-1", "Altomatehr", ("opt-1", "Office"))]);

        await service.SyncProjectsAsync();

        Assert.False(repo.Projects.Single().IsArchived);
    }

    // ---- wiring ----

    private static XeroTrackingCategoryResponse Category(
        string id, string name, params (string Id, string Name)[] options) =>
        new(id, name, "ACTIVE",
            [.. options.Select(o => new XeroTrackingOptionResponse(o.Id, o.Name, "ACTIVE"))]);

    private static XeroService Create(
        FakeXeroRepository repo, List<XeroTrackingCategoryResponse> categories)
    {
        var provider = DataProtectionProvider.Create("AltomateHR.Tests");
        string Protect(string v) => provider.CreateProtector("AltomateHR.XeroTokens.v1").Protect(v);

        repo.Connection = new XeroConnection
        {
            OrganizationId = "org-1",
            TenantId = "tenant-1",
            AccessTokenProtected = Protect("token"),
            RefreshTokenProtected = Protect("refresh"),
            AccessTokenExpiresAt = DateTime.UtcNow.AddHours(1),
        };

        return new XeroService(
            new FakeXeroCurrentUser(),
            repo,
            new FakeXeroProjectsClient([], categories),
            provider,
            Options.Create(new XeroOptions()),
            new FakeAuditService());
    }
}

// Xero Projects and tracking categories are different products, and an org that
// really uses Projects almost certainly also has tracking categories for
// regions or cost centres. Importing those as projects would be worse than the
// gap the fallback closes.
public class XeroTrackingFallbackOrderTests
{
    [Fact]
    public async Task SyncProjectsAsync_IgnoresTrackingCategories_WhenXeroProjectsExist()
    {
        var repo = new FakeXeroRepository();
        var provider = DataProtectionProvider.Create("AltomateHR.Tests");
        repo.Connection = new XeroConnection
        {
            OrganizationId = "org-1",
            TenantId = "tenant-1",
            AccessTokenProtected = provider.CreateProtector("AltomateHR.XeroTokens.v1").Protect("token"),
            RefreshTokenProtected = provider.CreateProtector("AltomateHR.XeroTokens.v1").Protect("refresh"),
            AccessTokenExpiresAt = DateTime.UtcNow.AddHours(1),
        };

        var service = new XeroService(
            new FakeXeroCurrentUser(),
            repo,
            new FakeXeroProjectsClient(
                [new XeroProjectResponse("x-1", "Client Site A", "INPROGRESS")],
                [new XeroTrackingCategoryResponse("cat-1", "Region", "ACTIVE",
                    [new XeroTrackingOptionResponse("opt-1", "North", "ACTIVE")])]),
            provider,
            Options.Create(new XeroOptions()),
            new FakeAuditService());

        await service.SyncProjectsAsync();

        Assert.Equal(["Client Site A"], repo.Projects.Select(p => p.Name));
    }
}
