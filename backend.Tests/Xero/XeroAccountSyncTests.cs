using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Accounts.Entities;
using AltomateHR.Api.Modules.Projects.Entities;
using AltomateHR.Api.Modules.Xero;
using AltomateHR.Api.Modules.Xero.Dtos;
using AltomateHR.Api.Modules.Xero.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

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
            Account("x-4", "1000", "Business account", "BANK"),
            // None of these belong in a claim's account picker.
            Account("x-5", "200", "Sales", "REVENUE"),
            Account("x-6", "610", "Accounts Receivable", "CURRENT"),
            Account("x-7", "800", "Accounts Payable", "CURRLIAB"),
            Account("x-8", "960", "Retained Earnings", "EQUITY"),
            Account("x-9", "710", "Office Equipment", "FIXED"),
        ]);

        var result = await service.SyncAccountsAsync();

        Assert.Equal(4, result.Imported);
        Assert.Equal(5, result.Skipped);
        Assert.Equal(
            ["6100", "6200", "6300", "1000"],
            repo.Accounts.Select(a => a.Code));
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
            Options.Create(new XeroOptions()));
    }

    private static readonly IDataProtectionProvider Provider =
        DataProtectionProvider.Create("AltomateHR.Tests");

    private static string Protect(string value) =>
        Provider.CreateProtector("AltomateHR.XeroTokens.v1").Protect(value);
}

internal sealed class FakeXeroCurrentUser : ICurrentUser
{
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

    public Task<ChartOfAccount?> GetAccountByXeroIdAsync(string organizationId, string xeroAccountId) =>
        Task.FromResult(Accounts.FirstOrDefault(a => a.XeroAccountId == xeroAccountId));

    public Task AddAccountAsync(ChartOfAccount account)
    {
        Accounts.Add(account);
        return Task.CompletedTask;
    }

    public Task UpdateAccountAsync(ChartOfAccount account) => Task.CompletedTask;

    public Task<XeroOAuthState> AddStateAsync(XeroOAuthState state) => throw new NotImplementedException();
    public Task<XeroOAuthState?> GetStateAsync(string state) => throw new NotImplementedException();
    public Task UpdateStateAsync(XeroOAuthState state) => throw new NotImplementedException();
    public Task<XeroConnection> UpsertConnectionAsync(XeroConnection c) => throw new NotImplementedException();
    public Task UpdateConnectionAsync(XeroConnection c) => Task.CompletedTask;
    public Task<Project?> GetProjectByXeroIdAsync(string o, string x) => throw new NotImplementedException();
    public Task AddProjectAsync(Project p) => throw new NotImplementedException();
    public Task UpdateProjectAsync(Project p) => throw new NotImplementedException();
}

internal sealed class FakeXeroAccountsClient : IXeroClient
{
    private readonly List<XeroAccountResponse> _accounts;

    public FakeXeroAccountsClient(List<XeroAccountResponse> accounts) => _accounts = accounts;

    public Task<List<XeroAccountResponse>> GetAccountsAsync(string accessToken, string tenantId) =>
        Task.FromResult(_accounts);

    public string BuildAuthorizationUrl(string state) => throw new NotImplementedException();
    public Task<XeroTokenResponse> ExchangeCodeAsync(string code) => throw new NotImplementedException();
    public Task<XeroTokenResponse> RefreshTokenAsync(string r) => throw new NotImplementedException();
    public Task<List<XeroTenantResponse>> GetTenantsAsync(string a) => throw new NotImplementedException();
    public Task<List<XeroProjectResponse>> GetProjectsAsync(string a, string t) => throw new NotImplementedException();
    public Task<XeroFileContent?> GetFileContentAsync(string a, string t, string f) => throw new NotImplementedException();
    public Task<XeroBillResponse> CreateBillAsync(string a, string t, XeroBillRequest b) => throw new NotImplementedException();
    public Task<XeroSpendResponse> CreateSpendAsync(string a, string t, XeroSpendRequest s) => throw new NotImplementedException();
}
