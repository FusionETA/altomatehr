using System.Text.Json;
using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Organizations.Entities;
using AltomateHR.Api.Modules.Xero;
using AltomateHR.Api.Modules.Xero.Dtos;
using AltomateHR.Api.Modules.Xero.Entities;
using AltomateHR.Api.Tests.Audit;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AltomateHR.Api.Tests.Xero;

// Which Xero app a company connects through is the superadmin's choice per
// owner; which app REFRESHES a connection is the app that issued it. Those
// two rules are what keep a company working while it is moved between apps.
public class XeroAppsTests : IDisposable
{
    private static readonly IDataProtectionProvider Provider = DataProtectionProvider.Create("AltomateHR.Tests");
    private static readonly XeroOptions Original = new()
    {
        ClientId = "ORIGINAL-CLIENT-ID", ClientSecret = "original-secret", RedirectUri = "https://api.test/xero/callback",
    };

    private readonly AppDbContext _db;
    private readonly StubCurrentUser _user = new() { UserId = "usr-super", OrganizationId = null };
    private readonly FakeAuditService _audit = new();

    public XeroAppsTests()
    {
        _db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"xero-apps-{Guid.NewGuid()}").Options,
            _user);

        _db.Users.AddRange(
            new User { Id = "usr-globe", Name = "Globe Owner", Email = "globe@x.test", PasswordHash = "x" },
            new User { Id = "usr-ayu", Name = "Ayu Owner", Email = "ayu@x.test", PasswordHash = "x" },
            new User { Id = "usr-other", Name = "Other Owner", Email = "other@x.test", PasswordHash = "x" });
        _db.Organizations.AddRange(
            new Organization { Id = "org-globe", Name = "Globe One" },
            new Organization { Id = "org-ayu", Name = "Ayu One" },
            new Organization { Id = "org-co", Name = "Co-owned" },
            new Organization { Id = "org-none", Name = "No owner" });
        _db.OrganizationMemberships.AddRange(
            Seat("org-globe", "usr-globe", 0),
            Seat("org-ayu", "usr-ayu", 0),
            Seat("org-co", "usr-other", 0),     // joined first
            Seat("org-co", "usr-ayu", 1));
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private static OrganizationMembership Seat(string org, string user, int order) => new()
    {
        OrganizationId = org, UserId = user, Role = "Owner", CreatedAt = DateTime.UtcNow.AddDays(-10 + order),
    };

    private XeroAppResolver Resolver() =>
        new(new XeroAppRepository(_db), Provider, Options.Create(Original));

    private XeroAppsService Apps() =>
        new(new XeroAppRepository(_db), _audit, _user, Provider, Options.Create(Original));

    private async Task<string> AddAppAsync(string name, string clientId, bool shared = false) =>
        (await Apps().AddAppAsync(new SaveXeroAppDto
        {
            Name = name, ClientId = clientId, ClientSecret = $"{name}-secret", IsShared = shared,
        })).Id;

    // ---- which app a company connects through ----

    [Fact]
    public async Task WithNoSharedAppAdded_EveryoneIsOnTheOriginalApp()
    {
        var app = await Resolver().ForConnectAsync("org-globe");

        Assert.Null(app.AppId);
        Assert.Equal(("ORIGINAL-CLIENT-ID", "original-secret"), (app.ClientId, app.ClientSecret));
    }

    [Fact]
    public async Task TheSharedApp_TheOriginalApp_AndAnOwnersOwnApp()
    {
        var shared = await AddAppAsync("Shared", "SHARED-CLIENT-ID", shared: true);
        var ayu = await AddAppAsync("Ayu", "AYU-CLIENT-ID");
        await Apps().SetOwnerChoiceAsync("usr-globe", new SetXeroOwnerChoiceDto { Choice = XeroAppChoice.Original });
        await Apps().SetOwnerChoiceAsync("usr-ayu", new SetXeroOwnerChoiceDto { Choice = XeroAppChoice.Standalone, XeroAppId = ayu });

        Assert.Null((await Resolver().ForConnectAsync("org-globe")).AppId);
        var ayuApp = await Resolver().ForConnectAsync("org-ayu");
        Assert.Equal((ayu, "AYU-CLIENT-ID", "Ayu-secret"), (ayuApp.AppId, ayuApp.ClientId, ayuApp.ClientSecret));
        Assert.Equal(shared, (await Resolver().ForConnectAsync("org-none")).AppId);   // no owner: shared
    }

    [Fact]
    public async Task OwnAppNotAddedYet_BlocksConnect()
    {
        await Apps().SetOwnerChoiceAsync("usr-ayu", new SetXeroOwnerChoiceDto { Choice = XeroAppChoice.Standalone });

        Assert.True((await Resolver().AssignmentAsync("org-ayu")).Blocked);
        var ex = await Assert.ThrowsAsync<XeroConnectionException>(() => Resolver().ForConnectAsync("org-ayu"));
        Assert.Equal(XeroAppResolver.BlockedMessage, ex.Message);
    }

    // A co-owned company follows the owner who joined first, until the
    // superadmin moves it — and only to someone who still owns it.
    [Fact]
    public async Task ACoOwnedCompany_FollowsTheOwnerPicked()
    {
        var ayu = await AddAppAsync("Ayu", "AYU-CLIENT-ID");
        await Apps().SetOwnerChoiceAsync("usr-ayu", new SetXeroOwnerChoiceDto { Choice = XeroAppChoice.Standalone, XeroAppId = ayu });

        Assert.Null((await Resolver().ForConnectAsync("org-co")).AppId);

        Assert.Null((await Apps().SetCompanyOwnerAsync("org-co", "usr-ayu")).Error);
        Assert.Equal(ayu, (await Resolver().ForConnectAsync("org-co")).AppId);

        Assert.NotNull((await Apps().SetCompanyOwnerAsync("org-co", "usr-globe")).Error);
    }

    // ---- an existing connection ----

    // Moving an owner must not break their connected companies: the tokens
    // still refresh with the app that issued them, and status says reconnect.
    [Fact]
    public async Task AMovedCompany_KeepsRefreshingThroughTheAppThatConnectedIt()
    {
        var ayu = await AddAppAsync("Ayu", "AYU-CLIENT-ID");
        await Apps().SetOwnerChoiceAsync("usr-ayu", new SetXeroOwnerChoiceDto { Choice = XeroAppChoice.Standalone, XeroAppId = ayu });

        var repo = new FakeXeroRepository
        {
            Connection = new XeroConnection
            {
                OrganizationId = "org-ayu", TenantId = "t-1", TenantName = "Ayu One",
                AccessTokenProtected = Provider.CreateProtector("AltomateHR.XeroTokens.v1").Protect("old"),
                RefreshTokenProtected = Provider.CreateProtector("AltomateHR.XeroTokens.v1").Protect("refresh"),
                AccessTokenExpiresAt = DateTime.UtcNow.AddMinutes(-5),
                XeroAppId = null,   // connected through the original app
            },
        };
        var client = new RecordingRefreshClient();
        var service = new XeroService(
            new AyuAdmin(), repo, client, Provider, Options.Create(Original), _audit, apps: Resolver());

        await service.GetCurrenciesAsync();
        Assert.Equal("ORIGINAL-CLIENT-ID", client.RefreshedWith?.ClientId);

        var status = await service.GetStatusAsync();
        Assert.True(status.Connected);
        Assert.True(status.ReconnectToMoveApp);
        Assert.True(status.OwnApp);
    }

    // Reconnecting through the owner's new app takes the OLD app out of the
    // Xero org's connected apps, using the old app's tokens, and the new
    // connection remembers the app that issued it.
    [Fact]
    public async Task ReconnectingThroughANewApp_RemovesTheOldOneInXero()
    {
        var ayu = await AddAppAsync("Ayu", "AYU-CLIENT-ID");
        await Apps().SetOwnerChoiceAsync("usr-ayu", new SetXeroOwnerChoiceDto { Choice = XeroAppChoice.Standalone, XeroAppId = ayu });

        var tokens = Provider.CreateProtector("AltomateHR.XeroTokens.v1");
        var repo = new FakeXeroRepository
        {
            Connection = new XeroConnection
            {
                OrganizationId = "org-ayu", TenantId = "t-1", TenantName = "Ayu One", ConnectionId = "conn-old",
                AccessTokenProtected = tokens.Protect("old-access"), RefreshTokenProtected = tokens.Protect("old-refresh"),
                AccessTokenExpiresAt = DateTime.UtcNow.AddHours(1), ConnectedAt = DateTime.UtcNow.AddMonths(-1),
                XeroAppId = null,
            },
        };
        repo.States.Add(new XeroOAuthState
        {
            OrganizationId = "org-ayu", State = "s-1", ExpiresAt = DateTime.UtcNow.AddMinutes(10), XeroAppId = ayu,
        });
        var client = new RecordingRefreshClient();
        var service = new XeroService(
            new AyuAdmin(), repo, client, Provider, Options.Create(Original), _audit, apps: Resolver());

        var url = await service.CompleteCallbackAsync("code-1", "s-1");

        Assert.Contains("xero=connected", url);
        Assert.Equal("AYU-CLIENT-ID", client.ExchangedWith?.ClientId);
        Assert.Equal(("old-access", "conn-old"), client.Deleted);
        Assert.Equal(ayu, repo.Connection!.XeroAppId);
    }

    // ---- the superadmin's screen ----

    [Fact]
    public async Task ASecret_IsNeverSentBack()
    {
        await AddAppAsync("Ayu", "AYU-CLIENT-ID-1234");

        var overview = await Apps().GetOverviewAsync();
        var json = JsonSerializer.Serialize(overview);

        Assert.DoesNotContain("Ayu-secret", json);
        Assert.DoesNotContain("AYU-CLIENT-ID-1234", json);
        Assert.Equal("…1234", Assert.Single(overview.Apps).ClientIdEnding);
        Assert.DoesNotContain("original-secret", json);
    }

    [Fact]
    public async Task TheOverview_GroupsCompaniesByOwner()
    {
        var overview = await Apps().GetOverviewAsync();

        Assert.Equal(["Ayu Owner", "Globe Owner", "Other Owner"], overview.Owners.Select(o => o.Name));
        Assert.Equal(["Co-owned"], overview.Owners.Single(o => o.Name == "Other Owner").Companies.Select(c => c.Name));
        Assert.Equal(["No owner"], overview.CompaniesWithoutOwner.Select(c => c.Name));
        Assert.True(overview.OriginalApp.IsShared);
    }

    [Fact]
    public async Task AnAppInUse_CantBeDeleted_OrHaveItsClientIdChanged()
    {
        var ayu = await AddAppAsync("Ayu", "AYU-CLIENT-ID");
        _db.XeroConnections.Add(new XeroConnection { OrganizationId = "org-ayu", TenantId = "t", XeroAppId = ayu });
        await _db.SaveChangesAsync();

        Assert.NotNull((await Apps().DeleteAppAsync(ayu)).Error);
        await Assert.ThrowsAsync<ArgumentException>(() => Apps().UpdateAppAsync(ayu, new SaveXeroAppDto
        {
            Name = "Ayu", ClientId = "ANOTHER-CLIENT-ID", ClientSecret = "s",
        }));

        // A new secret for the same app is fine: the client id is what issued the tokens.
        Assert.NotNull(await Apps().UpdateAppAsync(ayu, new SaveXeroAppDto
        {
            Name = "Ayu Borneo", ClientId = "AYU-CLIENT-ID", ClientSecret = "rotated",
        }));
        Assert.Equal("rotated", (await Resolver().ForAppAsync(ayu)).ClientSecret);
    }

    // An owner who no longer owns a company drops off the screen; their old
    // setting must not keep their app undeletable.
    [Fact]
    public async Task ASettingLeftBySomeoneWhoOwnsNothing_DoesntHoldAnAppUp()
    {
        var ayu = await AddAppAsync("Ayu", "AYU-CLIENT-ID");
        await Apps().SetOwnerChoiceAsync("usr-ayu", new SetXeroOwnerChoiceDto { Choice = XeroAppChoice.Standalone, XeroAppId = ayu });
        Assert.NotNull((await Apps().DeleteAppAsync(ayu)).Error);

        foreach (var seat in _db.OrganizationMemberships.IgnoreQueryFilters().Where(m => m.UserId == "usr-ayu"))
            seat.Role = "Admin";
        await _db.SaveChangesAsync();

        Assert.Equal(0, (await Apps().GetOverviewAsync()).Apps.Single().Owners);
        Assert.Null((await Apps().DeleteAppAsync(ayu)).Error);
    }

    // Written into customers' logs, so masked as support like every other
    // Fusioneta action there.
    [Fact]
    public async Task ChangesAreAuditedAsSupport()
    {
        await Apps().SetOwnerChoiceAsync("usr-globe", new SetXeroOwnerChoiceDto { Choice = XeroAppChoice.Original });

        var entry = Assert.Single(_audit.Written, e => e.OrganizationId == "org-globe");
        Assert.Equal("usr-super", entry.Support?.UserId);
    }

    [Fact]
    public async Task TheOriginalAppsClientId_CantBeAddedAgain()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => AddAppAsync("Copy", "ORIGINAL-CLIENT-ID"));
    }

    [Fact]
    public async Task OnlyOneAppIsShared()
    {
        var first = await AddAppAsync("First", "FIRST-CLIENT-ID", shared: true);
        var second = await AddAppAsync("Second", "SECOND-CLIENT-ID", shared: true);

        var apps = (await Apps().GetOverviewAsync()).Apps;
        Assert.False(apps.Single(a => a.Id == first).IsShared);
        Assert.True(apps.Single(a => a.Id == second).IsShared);
    }

    private sealed class AyuAdmin : AltomateHR.Api.Common.ICurrentUser
    {
        public string? Email => "admin@ayu.test";
        public string? UserId => "usr-ayu";
        public string? OrganizationId => "org-ayu";
        public string? Role => "Owner";
        public bool IsAdmin => true;
        public bool IsAuthenticated => true;
        public string? IpAddress => null;
    }

    private sealed class RecordingRefreshClient : IXeroClient
    {
        public XeroAppCredentials? RefreshedWith { get; private set; }
        public XeroAppCredentials? ExchangedWith { get; private set; }
        public (string Token, string ConnectionId)? Deleted { get; private set; }

        public Task<XeroTokenResponse> RefreshTokenAsync(XeroAppCredentials app, string refreshToken)
        {
            RefreshedWith = app;
            return Task.FromResult(new XeroTokenResponse("new", "new-refresh", 1800, "Bearer", "accounting.settings"));
        }

        public Task<List<XeroCurrencyResponse>> GetCurrenciesAsync(string accessToken, string tenantId) =>
            Task.FromResult(new List<XeroCurrencyResponse> { new("MYR", "Malaysian Ringgit") });

        public string BuildAuthorizationUrl(XeroAppCredentials app, string state) => throw new NotImplementedException();
        public Task<XeroTokenResponse> ExchangeCodeAsync(XeroAppCredentials app, string code)
        {
            ExchangedWith = app;
            return Task.FromResult(new XeroTokenResponse("new-access", "new-refresh", 1800, "Bearer", "accounting.settings"));
        }

        public Task<List<XeroTenantResponse>> GetTenantsAsync(string a) =>
            Task.FromResult(new List<XeroTenantResponse> { new("conn-new", "t-1", "Ayu One", "ORGANISATION", null) });

        public Task DeleteConnectionAsync(string a, string connectionId)
        {
            Deleted = (a, connectionId);
            return Task.CompletedTask;
        }
        public Task<List<XeroAccountResponse>> GetAccountsAsync(string a, string t) => throw new NotImplementedException();
        public Task<List<XeroProjectResponse>> GetProjectsAsync(string a, string t) => throw new NotImplementedException();
        public Task<XeroFileContent?> GetFileContentAsync(string a, string t, string f) => throw new NotImplementedException();
        public Task<XeroBillResponse> CreateBillAsync(string a, string t, XeroBillRequest b) => throw new NotImplementedException();
        public Task<XeroSpendResponse> CreateSpendAsync(string a, string t, XeroSpendRequest s) => throw new NotImplementedException();
        public Task<XeroManualJournalResponse> CreateManualJournalAsync(string a, string t, XeroManualJournalRequest j) => throw new NotImplementedException();
        public Task<List<XeroTrackingCategoryResponse>> GetTrackingCategoriesAsync(string a, string t) => throw new NotImplementedException();
        public Task<string?> EnsureFolderAsync(string accessToken, string tenantId, string folderName) => Task.FromResult<string?>(null);
        public Task AssociateFileAsync(string accessToken, string tenantId, string fileId, string objectId, XeroAttachTarget target) => Task.CompletedTask;
        public Task<XeroUploadedFile> UploadFileAsync(string accessToken, string tenantId, string? folderId, byte[] content, string fileName, string contentType) =>
            throw new NotSupportedException();
    }
}
