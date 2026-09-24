using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Accounts.Entities;
using AltomateHR.Api.Modules.Projects.Entities;
using AltomateHR.Api.Modules.Xero;
using AltomateHR.Api.Modules.Xero.Dtos;
using AltomateHR.Api.Modules.Xero.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

using AltomateHR.Api.Tests.Audit;

namespace AltomateHR.Api.Tests.Xero;

// A real Xero chart of accounts holds revenue, receivables, equity and
// liabilities alongside expenses. Importing the lot would fill the claim
// form's account picker with things nobody can spend against.
public class XeroAccountSyncTests
{
    [Fact]
    public async Task SyncAccountsAsync_ImportsOnlyExpenseFamilyAccountsAndBanks()
    {
        var repo = new FakeXeroRepository();
        var service = Create(repo, [
            Account("x-1", "6100", "Travel", "EXPENSE"),
            Account("x-2", "6200", "Cost of goods", "DIRECTCOSTS"),
            Account("x-3", "6300", "Rent", "OVERHEADS"),
            // Xero's fourth expense-family type. It was missing from the
            // filter, so a chart using it silently lost those accounts.
            Account("x-10", "6400", "Depreciation", "DEPRECIATN"),
            Account("x-4", "1000", "Business account", "BANK"),
            // None of these belong in a claim's account picker.
            Account("x-5", "200", "Sales", "REVENUE"),
            Account("x-6", "610", "Accounts Receivable", "CURRENT"),
            Account("x-7", "800", "Accounts Payable", "CURRLIAB"),
            Account("x-8", "960", "Retained Earnings", "EQUITY"),
            Account("x-9", "710", "Office Equipment", "FIXED"),
        ]);

        var result = await service.SyncAccountsAsync();

        Assert.Equal(5, result.Imported);
        Assert.Equal(5, result.Skipped);
        Assert.Equal(
            ["6100", "6200", "6300", "6400", "1000"],
            repo.Accounts.Select(a => a.Code));
    }

    // ---- accounts the connected Xero no longer has ----
    //
    // A chart pulled from the wrong Xero org (a test org's "310 Cost of Goods
    // Sold") stayed selectable in a real company's claim form, because the
    // sync only ever added and updated.

    [Fact]
    public async Task SyncAccountsAsync_RetiresAccountsTheConnectedXeroNoLongerHas()
    {
        var repo = new FakeXeroRepository();
        repo.Accounts.Add(new ChartOfAccount
        {
            Code = "310", Name = "Cost of Goods Sold", XeroAccountId = "x-test", IsSelectable = true,
        });
        var service = Create(repo, [Account("x-1", "6100", "Travel", "EXPENSE")]);

        var result = await service.SyncAccountsAsync();

        var gone = repo.Accounts.Single(a => a.XeroAccountId == "x-test");
        Assert.True(gone.IsArchived);
        Assert.False(gone.IsSelectable);
        Assert.Equal(1, result.Retired);
        // Retired, not removed: past claims still point at it.
        Assert.Equal(2, repo.Accounts.Count);
    }

    // Syncing while the WRONG Xero org is connected: most of the real chart is
    // "missing". Retiring it would stop every claim, so nothing is retired and
    // the sync says the connection looks wrong.
    [Fact]
    public async Task SyncAccountsAsync_RetiresNothingWhenMostOfTheChartIsMissing()
    {
        var repo = new FakeXeroRepository();
        foreach (var i in Enumerable.Range(1, 5))
            repo.Accounts.Add(new ChartOfAccount { Code = $"B-{i}", Name = $"Real {i}", XeroAccountId = $"x-real-{i}", IsSelectable = true });
        var service = Create(repo, [Account("x-test", "310", "Cost of Goods Sold", "EXPENSE")]);

        var result = await service.SyncAccountsAsync();

        Assert.Equal(0, result.Retired);
        Assert.Equal(5, result.WrongOrgSuspected);
        Assert.All(repo.Accounts.Where(a => a.Code.StartsWith("B-")), a => Assert.False(a.IsArchived));
    }

    // Custom accounts were never in Xero, so "not in Xero" says nothing.
    [Fact]
    public async Task SyncAccountsAsync_NeverRetiresACustomAccount()
    {
        var repo = new FakeXeroRepository();
        repo.Accounts.Add(new ChartOfAccount { Code = "C-1", Name = "Petty cash", IsCustom = true, IsSelectable = true });
        var service = Create(repo, [Account("x-1", "6100", "Travel", "EXPENSE")]);

        var result = await service.SyncAccountsAsync();

        var custom = repo.Accounts.Single(a => a.Code == "C-1");
        Assert.False(custom.IsArchived);
        Assert.True(custom.IsSelectable);
        Assert.Equal(0, result.Retired);
    }

    // An empty answer is far likelier a hiccup than an emptied chart — it must
    // not retire every account the company has.
    [Fact]
    public async Task SyncAccountsAsync_RetiresNothingWhenXeroReturnsNoAccounts()
    {
        var repo = new FakeXeroRepository();
        repo.Accounts.Add(new ChartOfAccount { Code = "6100", Name = "Travel", XeroAccountId = "x-1", IsSelectable = true });
        var service = Create(repo, []);

        var result = await service.SyncAccountsAsync();

        Assert.False(repo.Accounts.Single().IsArchived);
        Assert.Equal(0, result.Retired);
    }

    // Already retired: counted once, not on every later sync.
    [Fact]
    public async Task SyncAccountsAsync_DoesNotRecountAnAlreadyRetiredAccount()
    {
        var repo = new FakeXeroRepository();
        repo.Accounts.Add(new ChartOfAccount
        {
            Code = "310", Name = "Cost of Goods Sold", XeroAccountId = "x-test", IsArchived = true, IsSelectable = false,
        });
        var service = Create(repo, [Account("x-1", "6100", "Travel", "EXPENSE")]);

        Assert.Equal(0, (await service.SyncAccountsAsync()).Retired);
    }

    [Fact]
    public async Task SyncAccountsAsync_NeverMakesABankAccountSelectableForClaims()
    {
        var repo = new FakeXeroRepository();
        var service = Create(repo, [
            Account("x-1", "6100", "Travel", "EXPENSE"),
            Account("x-2", "1000", "Business account", "BANK"),
        ]);

        await service.SyncAccountsAsync();

        var expense = repo.Accounts.Single(a => a.Code == "6100");
        var bank = repo.Accounts.Single(a => a.Code == "1000");

        Assert.True(expense.IsSelectable);
        // A bank account is what company spend comes FROM, never what a claim
        // is coded to — so it must not appear in the employee's picker.
        Assert.False(bank.IsSelectable);
        Assert.Equal("BANK", bank.Type);
    }

    [Fact]
    public async Task SyncAccountsAsync_DeselectsABankAccountThatWasSelectableBefore()
    {
        var repo = new FakeXeroRepository();
        repo.Accounts.Add(new ChartOfAccount
        {
            OrganizationId = "org-1",
            Code = "1000",
            Name = "Business account",
            Type = "BANK",
            XeroAccountId = "x-2",
            IsSelectable = true,   // wrong, from an earlier import
        });

        var service = Create(repo, [Account("x-2", "1000", "Business account", "BANK")]);
        await service.SyncAccountsAsync();

        Assert.False(repo.Accounts.Single().IsSelectable);
    }

    [Fact]
    public async Task SyncAccountsAsync_RetiresAccountsAnEarlierUnfilteredSyncImported()
    {
        // What a sync before the type filter left behind: revenue sitting in
        // the claim form as a selectable expense.
        var repo = new FakeXeroRepository();
        repo.Accounts.Add(new ChartOfAccount
        {
            OrganizationId = "org-1",
            Code = "200",
            Name = "Sales",
            Type = "EXPENSE",
            XeroAccountId = "x-5",
            IsSelectable = true,
        });

        var service = Create(repo, [Account("x-5", "200", "Sales", "REVENUE")]);
        var result = await service.SyncAccountsAsync();

        var sales = repo.Accounts.Single();
        // Skipping alone would have left it selectable forever.
        Assert.False(sales.IsSelectable);
        Assert.True(sales.IsArchived);
        Assert.Equal(1, result.Skipped);
    }

    // ---- wiring ----

    private static XeroAccountResponse Account(string id, string code, string name, string type) =>
        new(id, code, name, type, "ACTIVE", false);

    private static XeroService Create(FakeXeroRepository repo, List<XeroAccountResponse> accounts)
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
            new FakeXeroAccountsClient(accounts),
            Provider,
            Options.Create(new XeroOptions()),
            new FakeAuditService());
    }

    private static readonly IDataProtectionProvider Provider =
        DataProtectionProvider.Create("AltomateHR.Tests");

    private static string Protect(string value) =>
        Provider.CreateProtector("AltomateHR.XeroTokens.v1").Protect(value);
}

internal sealed class FakeXeroCurrentUser : ICurrentUser
{
    public string? Email => "test@altomate.com";
    public string? UserId => "usr-admin";
    public string? OrganizationId => "org-1";
    public string? Role => "Owner";
    public bool IsAdmin => true;
    public bool IsAuthenticated => true;
    public string? IpAddress => null;
}

internal sealed class FakeXeroRepository : IXeroRepository
{
    public XeroConnection? Connection { get; set; }
    public List<ChartOfAccount> Accounts { get; } = [];

    public Task<XeroConnection?> GetConnectionAsync(string organizationId) =>
        Task.FromResult(Connection);

    // Xero orgs connected to OTHER companies — seeded by the callback tests.
    public HashSet<string> TenantsUsedElsewhere { get; } = [];
    public Task<HashSet<string>> GetTenantIdsConnectedElsewhereAsync(
        IEnumerable<string> tenantIds, string organizationId) =>
        Task.FromResult(tenantIds.Where(TenantsUsedElsewhere.Contains).ToHashSet());

    public Task<ChartOfAccount?> GetAccountByXeroIdAsync(string organizationId, string xeroAccountId) =>
        Task.FromResult(Accounts.FirstOrDefault(a => a.XeroAccountId == xeroAccountId));

    public Task<List<ChartOfAccount>> GetXeroSourcedAccountsAsync(string organizationId) =>
        Task.FromResult(Accounts.Where(a => a.XeroAccountId != null).ToList());

    public Task AddAccountAsync(ChartOfAccount account)
    {
        Accounts.Add(account);
        return Task.CompletedTask;
    }

    public Task UpdateAccountAsync(ChartOfAccount account) => Task.CompletedTask;

    public Task<XeroOAuthState> AddStateAsync(XeroOAuthState state) => throw new NotImplementedException();
    // No state was ever issued unless a test adds one — which is exactly the
    // "unknown state" the callback has to reject.
    public List<XeroOAuthState> States { get; } = [];
    public Task<XeroOAuthState?> GetStateAsync(string state) =>
        Task.FromResult(States.FirstOrDefault(s => s.State == state));
    public Task UpdateStateAsync(XeroOAuthState state) => throw new NotImplementedException();
    public Task<XeroConnection> UpsertConnectionAsync(XeroConnection c) => throw new NotImplementedException();
    public Task UpdateConnectionAsync(XeroConnection c) => Task.CompletedTask;
    public List<Project> Projects { get; } = [];

    public Task<Project?> GetProjectByXeroIdAsync(string o, string x) =>
        Task.FromResult(Projects.FirstOrDefault(p => p.XeroProjectId == x));

    public Task<Project?> GetProjectByTrackingOptionAsync(string o, string optionId) =>
        Task.FromResult(Projects.FirstOrDefault(p => p.XeroTrackingOptionId == optionId));

    public Task AddProjectAsync(Project p)
    {
        Projects.Add(p);
        return Task.CompletedTask;
    }

    public Task UpdateProjectAsync(Project p) => Task.CompletedTask;

    // Mirrors the SQL in the real repository: hand-created (no XeroProjectId)
    // and not already archived by someone.
    public Task<int> ArchiveManualProjectsAsync(string organizationId)
    {
        var hit = Projects
            .Where(p => p.XeroProjectId is null && p.XeroTrackingOptionId is null && !p.IsArchived)
            .ToList();
        foreach (var p in hit)
        {
            p.IsArchived = true;
            p.ArchivedByXeroConnect = true;
        }
        return Task.FromResult(hit.Count);
    }

    public Task<int> RestoreProjectsArchivedByXeroConnectAsync(string organizationId)
    {
        var hit = Projects.Where(p => p.ArchivedByXeroConnect).ToList();
        foreach (var p in hit)
        {
            p.IsArchived = false;
            p.ArchivedByXeroConnect = false;
        }
        return Task.FromResult(hit.Count);
    }
}

internal sealed class FakeXeroAccountsClient : IXeroClient
{
    public Task<List<XeroCurrencyResponse>> GetCurrenciesAsync(string accessToken, string tenantId) =>
        Task.FromResult(new List<XeroCurrencyResponse> { new("MYR", "Malaysian Ringgit") });

    private readonly List<XeroAccountResponse> _accounts;

    public FakeXeroAccountsClient(List<XeroAccountResponse> accounts) => _accounts = accounts;

    public Task<List<XeroAccountResponse>> GetAccountsAsync(string accessToken, string tenantId) =>
        Task.FromResult(_accounts);

    public string BuildAuthorizationUrl(string state) => throw new NotImplementedException();
    public Task<XeroTokenResponse> ExchangeCodeAsync(string code) => throw new NotImplementedException();
    public Task<XeroTokenResponse> RefreshTokenAsync(string r) => throw new NotImplementedException();
    public Task<List<XeroTenantResponse>> GetTenantsAsync(string a) => throw new NotImplementedException();
    public Task DeleteConnectionAsync(string a, string connectionId) => throw new NotImplementedException();
    public Task<List<XeroProjectResponse>> GetProjectsAsync(string a, string t) => throw new NotImplementedException();
    public Task<XeroFileContent?> GetFileContentAsync(string a, string t, string f) => throw new NotImplementedException();
    public Task<XeroBillResponse> CreateBillAsync(string a, string t, XeroBillRequest b) => throw new NotImplementedException();
    public Task<XeroSpendResponse> CreateSpendAsync(string a, string t, XeroSpendRequest s) => throw new NotImplementedException();

    // Payroll posts through these; nothing in this file's scenarios does.
    public Task<XeroManualJournalResponse> CreateManualJournalAsync(
        string accessToken, string tenantId, XeroManualJournalRequest journal) =>
        throw new NotSupportedException();

    public Task<List<XeroTrackingCategoryResponse>> GetTrackingCategoriesAsync(
        string accessToken, string tenantId) =>
        Task.FromResult(new List<XeroTrackingCategoryResponse>());

    // Files API: nothing under test here uploads.
    public Task<string?> EnsureFolderAsync(string accessToken, string tenantId, string folderName) =>
        Task.FromResult<string?>(null);

    public Task<XeroUploadedFile> UploadFileAsync(
        string accessToken, string tenantId, string? folderId,
        byte[] content, string fileName, string contentType) =>
        throw new NotSupportedException();
}
