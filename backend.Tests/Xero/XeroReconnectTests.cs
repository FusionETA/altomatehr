using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Xero;
using AltomateHR.Api.Modules.Xero.Dtos;
using AltomateHR.Api.Modules.Xero.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

using AltomateHR.Api.Tests.Audit;

namespace AltomateHR.Api.Tests.Xero;

// A Xero connection dies in two ways the app cannot prevent: Xero revokes or
// expires the refresh token (60 days idle), or the data-protection key ring that
// encrypted the stored tokens is gone. Both used to surface as an unhandled
// CryptographicException / XeroConnectionException — a raw 500 on every call,
// with the settings page still reporting a healthy connection.
//
// The distinction that matters here is permanent vs transient: a dead credential
// must retire the connection and ask for consent, while Xero merely being down
// must leave a working connection alone.
public class XeroReconnectTests
{
    [Fact]
    public async Task WhenXeroRejectsTheRefreshToken_FlagsAReconnectWithoutDisconnecting()
    {
        var repo = ConnectedRepo(accessTokenExpired: true);
        var service = Create(repo, new FakeRefreshFailureClient(
            new XeroConnectionException("Xero token exchange failed. invalid_grant.", 400)));

        await Assert.ThrowsAsync<XeroReconnectRequiredException>(() => service.GetCurrenciesAsync());

        // Flagged, NOT disconnected: the row stays ours so the settings page can
        // still name the Xero org the admin has to reconnect to.
        Assert.NotNull(repo.Connection!.ReconnectRequiredAt);
        Assert.Null(repo.Connection.DisconnectedAt);
        Assert.True(repo.Connection.IsConnected);

        var status = await service.GetStatusAsync();
        Assert.True(status.Connected);
        Assert.True(status.NeedsReconnect);
        Assert.Equal("AltomateHR-Test", status.TenantName);
    }

    // The whole point of the change: whichever way the connection died, the
    // stored state and what the API reports are identical.
    [Fact]
    public async Task BothFailureModesLeaveTheSameState()
    {
        var revoked = ConnectedRepo(accessTokenExpired: true);
        await Assert.ThrowsAsync<XeroReconnectRequiredException>(() =>
            Create(revoked, new FakeRefreshFailureClient(
                new XeroConnectionException("invalid_grant.", 400))).GetCurrenciesAsync());

        var lostKeys = ConnectedRepo(accessTokenExpired: true);
        lostKeys.Connection!.AccessTokenProtected = ProtectWith("a-lost-key-ring", "token");
        lostKeys.Connection.RefreshTokenProtected = ProtectWith("a-lost-key-ring", "refresh");
        var lostKeysService = Create(lostKeys, new FakeRefreshFailureClient(
            new XeroConnectionException("should never be reached", 400)));
        await Assert.ThrowsAsync<XeroReconnectRequiredException>(() => lostKeysService.GetCurrenciesAsync());

        foreach (var repo in new[] { revoked, lostKeys })
        {
            Assert.NotNull(repo.Connection!.ReconnectRequiredAt);
            Assert.Null(repo.Connection.DisconnectedAt);
            Assert.True(repo.Connection.IsConnected);
        }

        var a = await Create(revoked, new FakeRefreshFailureClient(new Exception())).GetStatusAsync();
        var b = await lostKeysService.GetStatusAsync();
        Assert.Equal((a.Connected, a.NeedsReconnect), (b.Connected, b.NeedsReconnect));
    }

    [Fact]
    public async Task AFlaggedConnectionIsAuditedOnceRatherThanOnEveryCall()
    {
        var repo = ConnectedRepo(accessTokenExpired: true);
        var audit = new FakeAuditService();
        var service = Create(repo, new FakeRefreshFailureClient(
            new XeroConnectionException("invalid_grant.", 400)), audit);

        for (var i = 0; i < 3; i++)
            await Assert.ThrowsAsync<XeroReconnectRequiredException>(() => service.GetCurrenciesAsync());

        Assert.Single(audit.Written, e => e.Action == AuditActions.XeroReconnectRequired);
    }

    [Fact]
    public async Task ARefreshThatSucceedsClearsAnEarlierReconnectFlag()
    {
        var repo = ConnectedRepo(accessTokenExpired: true);
        repo.Connection!.ReconnectRequiredAt = DateTime.UtcNow.AddDays(-1);   // left by an earlier failure
        var service = Create(repo, new FakeWorkingRefreshClient());

        Assert.Equal(["MYR"], (await service.GetCurrenciesAsync()).Select(c => c.Code));

        // The banner must not outlive the problem.
        Assert.Null(repo.Connection.ReconnectRequiredAt);
        Assert.False((await service.GetStatusAsync()).NeedsReconnect);
    }

    [Fact]
    public async Task WhenXeroIsTemporarilyDown_KeepsTheConnectionAndDoesNotAskForReconnect()
    {
        var repo = ConnectedRepo(accessTokenExpired: true);
        var service = Create(repo, new FakeRefreshFailureClient(
            new XeroConnectionException("Xero token exchange failed. Xero returned 503.", 503)));

        // A retryable fault, so it stays a XeroConnectionException...
        var ex = await Assert.ThrowsAsync<XeroConnectionException>(() => service.GetCurrenciesAsync());
        Assert.IsNotType<XeroReconnectRequiredException>(ex);

        // ...and must NOT tear down a connection that is still perfectly good.
        Assert.Null(repo.Connection!.DisconnectedAt);
        Assert.True(repo.Connection.IsConnected);
    }

    [Fact]
    public async Task WhenTheKeyRingCannotDecryptTheTokens_ReportsNeedsReconnectAndAsksForReconnect()
    {
        // Tokens written by a container whose key ring is gone.
        var repo = ConnectedRepo(accessTokenExpired: false);
        repo.Connection!.AccessTokenProtected = ProtectWith("a-lost-key-ring", "token");
        repo.Connection.RefreshTokenProtected = ProtectWith("a-lost-key-ring", "refresh");

        var service = Create(repo, new FakeRefreshFailureClient(
            new XeroConnectionException("should never be reached", 400)));

        // Surfaced on status without calling Xero at all, so the settings page
        // prompts instead of looking healthy until the next sync fails — and it
        // reads true even before anything has tried to use the tokens.
        var status = await service.GetStatusAsync();
        Assert.True(status.Connected);
        Assert.True(status.NeedsReconnect);
        Assert.Null(repo.Connection.ReconnectRequiredAt);   // nothing has failed yet

        await Assert.ThrowsAsync<XeroReconnectRequiredException>(() => service.GetCurrenciesAsync());

        // ...and once a real call has failed, it is recorded like the other mode.
        Assert.NotNull(repo.Connection.ReconnectRequiredAt);
        Assert.Null(repo.Connection.DisconnectedAt);
    }

    [Fact]
    public async Task WhenTheTokensStillDecrypt_DoesNotAskForReconnect()
    {
        var repo = ConnectedRepo(accessTokenExpired: false);
        var service = Create(repo, new FakeRefreshFailureClient(
            new XeroConnectionException("should never be reached", 400)));

        var status = await service.GetStatusAsync();

        Assert.True(status.Connected);
        Assert.False(status.NeedsReconnect);
        // A live access token needs no refresh, so the failing client is untouched.
        Assert.Equal(["MYR"], (await service.GetCurrenciesAsync()).Select(c => c.Code));
    }

    private static FakeXeroRepository ConnectedRepo(bool accessTokenExpired) => new()
    {
        Connection = new XeroConnection
        {
            OrganizationId = "org-1",
            TenantId = "tenant-1",
            TenantName = "AltomateHR-Test",
            AccessTokenProtected = Protect("token"),
            RefreshTokenProtected = Protect("refresh"),
            AccessTokenExpiresAt = accessTokenExpired
                ? DateTime.UtcNow.AddMinutes(-5)
                : DateTime.UtcNow.AddHours(1),
        },
    };

    private static XeroService Create(
        FakeXeroRepository repo, IXeroClient client, FakeAuditService? audit = null) =>
        new(new FakeXeroCurrentUser(),
            repo,
            client,
            Provider,
            Options.Create(new XeroOptions()),
            audit ?? new FakeAuditService());

    private static readonly IDataProtectionProvider Provider =
        DataProtectionProvider.Create("AltomateHR.Tests");

    private static string Protect(string value) =>
        Provider.CreateProtector("AltomateHR.XeroTokens.v1").Protect(value);

    private static string ProtectWith(string appName, string value) =>
        DataProtectionProvider.Create(appName)
            .CreateProtector("AltomateHR.XeroTokens.v1")
            .Protect(value);
}

// Fails only the refresh, with a caller-chosen error. Everything else behaves,
// so a test that gets past the refresh still reaches a working Xero.
internal sealed class FakeRefreshFailureClient : IXeroClient
{
    private readonly Exception _onRefresh;

    public FakeRefreshFailureClient(Exception onRefresh) => _onRefresh = onRefresh;

    public Task<XeroTokenResponse> RefreshTokenAsync(string refreshToken) =>
        throw _onRefresh;

    public Task<List<XeroCurrencyResponse>> GetCurrenciesAsync(string accessToken, string tenantId) =>
        Task.FromResult(new List<XeroCurrencyResponse> { new("MYR", "Malaysian Ringgit") });

    public string BuildAuthorizationUrl(string state) => throw new NotImplementedException();
    public Task<XeroTokenResponse> ExchangeCodeAsync(string code) => throw new NotImplementedException();
    public Task<List<XeroTenantResponse>> GetTenantsAsync(string a) => throw new NotImplementedException();
    public Task<List<XeroAccountResponse>> GetAccountsAsync(string a, string t) => throw new NotImplementedException();
    public Task<List<XeroProjectResponse>> GetProjectsAsync(string a, string t) => throw new NotImplementedException();
    public Task<XeroFileContent?> GetFileContentAsync(string a, string t, string f) => throw new NotImplementedException();
    public Task<XeroBillResponse> CreateBillAsync(string a, string t, XeroBillRequest b) => throw new NotImplementedException();
    public Task<XeroSpendResponse> CreateSpendAsync(string a, string t, XeroSpendRequest s) => throw new NotImplementedException();
    public Task<XeroManualJournalResponse> CreateManualJournalAsync(string a, string t, XeroManualJournalRequest j) => throw new NotImplementedException();
    public Task<List<XeroTrackingCategoryResponse>> GetTrackingCategoriesAsync(string a, string t) => throw new NotImplementedException();
}

// Refreshes successfully, so the healthy path can be asserted too.
internal sealed class FakeWorkingRefreshClient : IXeroClient
{
    public Task<XeroTokenResponse> RefreshTokenAsync(string refreshToken) =>
        Task.FromResult(new XeroTokenResponse("new-access", "new-refresh", 1800, "Bearer", "accounting.settings"));

    public Task<List<XeroCurrencyResponse>> GetCurrenciesAsync(string accessToken, string tenantId) =>
        Task.FromResult(new List<XeroCurrencyResponse> { new("MYR", "Malaysian Ringgit") });

    public string BuildAuthorizationUrl(string state) => throw new NotImplementedException();
    public Task<XeroTokenResponse> ExchangeCodeAsync(string code) => throw new NotImplementedException();
    public Task<List<XeroTenantResponse>> GetTenantsAsync(string a) => throw new NotImplementedException();
    public Task<List<XeroAccountResponse>> GetAccountsAsync(string a, string t) => throw new NotImplementedException();
    public Task<List<XeroProjectResponse>> GetProjectsAsync(string a, string t) => throw new NotImplementedException();
    public Task<XeroFileContent?> GetFileContentAsync(string a, string t, string f) => throw new NotImplementedException();
    public Task<XeroBillResponse> CreateBillAsync(string a, string t, XeroBillRequest b) => throw new NotImplementedException();
    public Task<XeroSpendResponse> CreateSpendAsync(string a, string t, XeroSpendRequest s) => throw new NotImplementedException();
    public Task<XeroManualJournalResponse> CreateManualJournalAsync(string a, string t, XeroManualJournalRequest j) => throw new NotImplementedException();
    public Task<List<XeroTrackingCategoryResponse>> GetTrackingCategoriesAsync(string a, string t) => throw new NotImplementedException();
}
