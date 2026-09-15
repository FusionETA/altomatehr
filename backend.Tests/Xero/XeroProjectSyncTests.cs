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

    public FakeXeroProjectsClient(List<XeroProjectResponse> projects) => _projects = projects;

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
        Task.FromResult(new List<XeroTrackingCategoryResponse>());
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
