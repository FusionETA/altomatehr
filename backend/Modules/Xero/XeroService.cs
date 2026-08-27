using System.Security.Cryptography;
using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Accounts.Entities;
using AltomateHR.Api.Modules.Projects.Entities;
using AltomateHR.Api.Modules.Xero.Dtos;
using AltomateHR.Api.Modules.Xero.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace AltomateHR.Api.Modules.Xero;

public class XeroService : IXeroService
{
    private readonly ICurrentUser _currentUser;
    private readonly IXeroRepository _repo;
    private readonly IXeroClient _client;
    private readonly IDataProtector _protector;
    private readonly XeroOptions _options;

    public XeroService(
        ICurrentUser currentUser,
        IXeroRepository repo,
        IXeroClient client,
        IDataProtectionProvider dataProtection,
        IOptions<XeroOptions> options)
    {
        _currentUser = currentUser;
        _repo = repo;
        _client = client;
        _protector = dataProtection.CreateProtector("AltomateHR.XeroTokens.v1");
        _options = options.Value;
    }

    public async Task<XeroConnectUrlDto> CreateConnectUrlAsync(string? returnUrl)
    {
        var orgId = RequireOrganization();
        var userId = _currentUser.UserId ?? throw new XeroConnectionException("Current user is missing.");
        var stateValue = CreateStateValue();
        var now = DateTime.UtcNow;

        await _repo.AddStateAsync(new XeroOAuthState
        {
            OrganizationId = orgId,
            UserId = userId,
            State = stateValue,
            ReturnUrl = string.IsNullOrWhiteSpace(returnUrl) ? null : returnUrl,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(15),
        });

        return new XeroConnectUrlDto { Url = _client.BuildAuthorizationUrl(stateValue) };
    }

    public async Task<string> CompleteCallbackAsync(string code, string state)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
            return _options.FailureRedirectUrl;

        var storedState = await _repo.GetStateAsync(state);
        if (storedState is null || storedState.UsedAt is not null || storedState.ExpiresAt < DateTime.UtcNow)
            return _options.FailureRedirectUrl;

        var token = await _client.ExchangeCodeAsync(code);
        var tenants = await _client.GetTenantsAsync(token.AccessToken);
        var tenant = tenants.FirstOrDefault()
            ?? throw new XeroConnectionException("No Xero organization was returned for this connection.");

        var now = DateTime.UtcNow;
        await _repo.UpsertConnectionAsync(new XeroConnection
        {
            OrganizationId = storedState.OrganizationId,
            ConnectionId = string.IsNullOrWhiteSpace(tenant.Id) ? null : tenant.Id,
            TenantId = tenant.TenantId,
            TenantName = tenant.TenantName,
            TenantType = tenant.TenantType,
            TokenType = token.TokenType,
            Scope = token.Scope,
            AccessTokenProtected = _protector.Protect(token.AccessToken),
            RefreshTokenProtected = _protector.Protect(token.RefreshToken),
            AccessTokenExpiresAt = now.AddSeconds(Math.Max(60, token.ExpiresIn - 60)),
            ConnectedAt = now,
            UpdatedAt = now,
        });

        storedState.UsedAt = now;
        await _repo.UpdateStateAsync(storedState);

        return storedState.ReturnUrl ?? _options.SuccessRedirectUrl;
    }

    public async Task<XeroStatusDto> GetStatusAsync()
    {
        var connection = await GetCurrentConnectionAsync();
        if (connection is null || !connection.IsConnected)
            return new XeroStatusDto { Connected = false };

        return new XeroStatusDto
        {
            Connected = true,
            TenantId = connection.TenantId,
            TenantName = connection.TenantName,
            ConnectedAt = connection.ConnectedAt,
            UpdatedAt = connection.UpdatedAt,
            AccessTokenExpiresAt = connection.AccessTokenExpiresAt,
        };
    }

    public async Task DisconnectAsync()
    {
        var connection = await GetCurrentConnectionAsync();
        if (connection is null) return;

        connection.DisconnectedAt = DateTime.UtcNow;
        connection.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateConnectionAsync(connection);
    }

    public async Task<XeroSyncAccountsResultDto> SyncAccountsAsync()
    {
        var orgId = RequireOrganization();
        var connection = await GetCurrentConnectionAsync()
            ?? throw new XeroConnectionException("Connect Xero before syncing accounts.");

        if (!connection.IsConnected)
            throw new XeroConnectionException("Xero is disconnected.");

        var accessToken = await GetValidAccessTokenAsync(connection);
        var xeroAccounts = await _client.GetAccountsAsync(accessToken, connection.TenantId);
        var result = new XeroSyncAccountsResultDto();
        var now = DateTime.UtcNow;

        foreach (var xeroAccount in xeroAccounts)
        {
            if (!ShouldImportAccount(xeroAccount))
            {
                result.Skipped++;
                continue;
            }

            var existing = await _repo.GetAccountByXeroIdAsync(orgId, xeroAccount.AccountId);
            if (existing is null)
            {
                await _repo.AddAccountAsync(new ChartOfAccount
                {
                    OrganizationId = orgId,
                    Code = xeroAccount.Code,
                    Name = xeroAccount.Name,
                    Type = ToLocalAccountType(xeroAccount.Type),
                    XeroAccountId = xeroAccount.AccountId,
                    XeroStatus = xeroAccount.Status,
                    XeroSyncedAt = now,
                    IsSelectable = IsActive(xeroAccount.Status),
                    CreatedAt = now,
                });
                result.Imported++;
                continue;
            }

            existing.Code = xeroAccount.Code;
            existing.Name = xeroAccount.Name;
            existing.Type = ToLocalAccountType(xeroAccount.Type);
            existing.XeroStatus = xeroAccount.Status;
            existing.XeroSyncedAt = now;
            existing.IsArchived = !IsActive(xeroAccount.Status);
            await _repo.UpdateAccountAsync(existing);
            result.Updated++;
        }

        return result;
    }

    public async Task<XeroSyncProjectsResultDto> SyncProjectsAsync()
    {
        var orgId = RequireOrganization();
        var connection = await GetCurrentConnectionAsync()
            ?? throw new XeroConnectionException("Connect Xero before syncing projects.");

        if (!connection.IsConnected)
            throw new XeroConnectionException("Xero is disconnected.");

        var accessToken = await GetValidAccessTokenAsync(connection);
        var xeroProjects = await _client.GetProjectsAsync(accessToken, connection.TenantId);
        var result = new XeroSyncProjectsResultDto();
        var now = DateTime.UtcNow;

        foreach (var xeroProject in xeroProjects)
        {
            if (!ShouldImportProject(xeroProject))
            {
                result.Skipped++;
                continue;
            }

            var existing = await _repo.GetProjectByXeroIdAsync(orgId, xeroProject.ProjectId);
            if (existing is null)
            {
                await _repo.AddProjectAsync(new Project
                {
                    OrganizationId = orgId,
                    Name = xeroProject.Name,
                    XeroProjectId = xeroProject.ProjectId,
                    XeroStatus = xeroProject.Status,
                    XeroSyncedAt = now,
                    IsArchived = IsClosedProject(xeroProject.Status),
                    CreatedAt = now,
                });
                result.Imported++;
                continue;
            }

            existing.Name = xeroProject.Name;
            existing.XeroStatus = xeroProject.Status;
            existing.XeroSyncedAt = now;
            existing.IsArchived = IsClosedProject(xeroProject.Status);
            await _repo.UpdateProjectAsync(existing);
            result.Updated++;
        }

        return result;
    }

    private async Task<string> GetValidAccessTokenAsync(XeroConnection connection)
    {
        if (connection.AccessTokenExpiresAt > DateTime.UtcNow.AddMinutes(2))
            return _protector.Unprotect(connection.AccessTokenProtected);

        var refreshToken = _protector.Unprotect(connection.RefreshTokenProtected);
        var refreshed = await _client.RefreshTokenAsync(refreshToken);
        var now = DateTime.UtcNow;

        connection.AccessTokenProtected = _protector.Protect(refreshed.AccessToken);
        connection.RefreshTokenProtected = _protector.Protect(refreshed.RefreshToken);
        connection.AccessTokenExpiresAt = now.AddSeconds(Math.Max(60, refreshed.ExpiresIn - 60));
        connection.TokenType = refreshed.TokenType;
        connection.Scope = refreshed.Scope;
        connection.UpdatedAt = now;
        await _repo.UpdateConnectionAsync(connection);

        return refreshed.AccessToken;
    }

    public async Task<XeroFileContent?> GetFileContentAsync(string fileId)
    {
        var connection = await GetCurrentConnectionAsync();
        if (connection is null || !connection.IsConnected) return null;

        var accessToken = await GetValidAccessTokenAsync(connection);
        return await _client.GetFileContentAsync(accessToken, connection.TenantId, fileId);
    }

    private async Task<XeroConnection?> GetCurrentConnectionAsync() =>
        await _repo.GetConnectionAsync(RequireOrganization());

    private string RequireOrganization() =>
        _currentUser.OrganizationId ?? throw new XeroConnectionException("Current organization is missing.");

    private static string CreateStateValue()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal)
            .TrimEnd('=');
    }

    private static bool ShouldImportAccount(XeroAccountResponse account) =>
        IsActive(account.Status) || string.Equals(account.Status, "ARCHIVED", StringComparison.OrdinalIgnoreCase);

    private static bool IsActive(string status) =>
        string.Equals(status, "ACTIVE", StringComparison.OrdinalIgnoreCase);

    private static string ToLocalAccountType(string xeroType) =>
        string.Equals(xeroType, "BANK", StringComparison.OrdinalIgnoreCase) ? "BANK" : "EXPENSE";

    private static bool ShouldImportProject(XeroProjectResponse project) =>
        !string.Equals(project.Status, "DELETED", StringComparison.OrdinalIgnoreCase);

    private static bool IsClosedProject(string status) =>
        string.Equals(status, "CLOSED", StringComparison.OrdinalIgnoreCase);
}
