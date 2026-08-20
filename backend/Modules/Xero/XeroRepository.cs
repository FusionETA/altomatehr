using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Accounts.Entities;
using AltomateHR.Api.Modules.Projects.Entities;
using AltomateHR.Api.Modules.Xero.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Xero;

public class XeroRepository : IXeroRepository
{
    private readonly AppDbContext _db;

    public XeroRepository(AppDbContext db) => _db = db;

    public async Task<XeroOAuthState> AddStateAsync(XeroOAuthState state)
    {
        _db.XeroOAuthStates.Add(state);
        await _db.SaveChangesAsync();
        return state;
    }

    public Task<XeroOAuthState?> GetStateAsync(string state) =>
        _db.XeroOAuthStates.FirstOrDefaultAsync(s => s.State == state);

    public async Task UpdateStateAsync(XeroOAuthState state)
    {
        _db.XeroOAuthStates.Update(state);
        await _db.SaveChangesAsync();
    }

    public Task<XeroConnection?> GetConnectionAsync(string organizationId) =>
        _db.XeroConnections.FirstOrDefaultAsync(c => c.OrganizationId == organizationId);

    public async Task<XeroConnection> UpsertConnectionAsync(XeroConnection connection)
    {
        var existing = await GetConnectionAsync(connection.OrganizationId);
        if (existing is null)
        {
            _db.XeroConnections.Add(connection);
            await _db.SaveChangesAsync();
            return connection;
        }

        existing.ConnectionId = connection.ConnectionId;
        existing.TenantId = connection.TenantId;
        existing.TenantName = connection.TenantName;
        existing.TenantType = connection.TenantType;
        existing.TokenType = connection.TokenType;
        existing.Scope = connection.Scope;
        existing.AccessTokenProtected = connection.AccessTokenProtected;
        existing.RefreshTokenProtected = connection.RefreshTokenProtected;
        existing.AccessTokenExpiresAt = connection.AccessTokenExpiresAt;
        existing.UpdatedAt = connection.UpdatedAt;
        existing.DisconnectedAt = null;
        await _db.SaveChangesAsync();
        return existing;
    }

    public async Task UpdateConnectionAsync(XeroConnection connection)
    {
        _db.XeroConnections.Update(connection);
        await _db.SaveChangesAsync();
    }

    public Task<ChartOfAccount?> GetAccountByXeroIdAsync(string organizationId, string xeroAccountId) =>
        _db.ChartOfAccounts.FirstOrDefaultAsync(a =>
            a.OrganizationId == organizationId && a.XeroAccountId == xeroAccountId);

    public async Task AddAccountAsync(ChartOfAccount account)
    {
        _db.ChartOfAccounts.Add(account);
        await _db.SaveChangesAsync();
    }

    public async Task UpdateAccountAsync(ChartOfAccount account)
    {
        _db.ChartOfAccounts.Update(account);
        await _db.SaveChangesAsync();
    }

    public Task<Project?> GetProjectByXeroIdAsync(string organizationId, string xeroProjectId) =>
        _db.Projects.FirstOrDefaultAsync(p =>
            p.OrganizationId == organizationId && p.XeroProjectId == xeroProjectId);

    public async Task AddProjectAsync(Project project)
    {
        _db.Projects.Add(project);
        await _db.SaveChangesAsync();
    }

    public async Task UpdateProjectAsync(Project project)
    {
        _db.Projects.Update(project);
        await _db.SaveChangesAsync();
    }
}
