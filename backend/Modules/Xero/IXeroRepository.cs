using AltomateHR.Api.Modules.Accounts.Entities;
using AltomateHR.Api.Modules.Projects.Entities;
using AltomateHR.Api.Modules.Xero.Entities;

namespace AltomateHR.Api.Modules.Xero;

public interface IXeroRepository
{
    Task<XeroOAuthState> AddStateAsync(XeroOAuthState state);
    Task<XeroOAuthState?> GetStateAsync(string state);
    Task UpdateStateAsync(XeroOAuthState state);

    // This person's latest sign-in still waiting for them to pick the Xero
    // org, in the current company, or null (none, or it has expired).
    Task<XeroOAuthState?> GetPendingChoiceAsync(string organizationId, string userId);
    Task<XeroConnection?> GetConnectionAsync(string organizationId);

    // Of these Xero orgs, the ones actively connected to a DIFFERENT
    // AltomateHR company. Crosses tenants on purpose — it is the check that
    // keeps one company's Xero off another's — so it ignores the org filter.
    Task<HashSet<string>> GetTenantIdsConnectedElsewhereAsync(
        IEnumerable<string> tenantIds, string organizationId);
    Task<XeroConnection> UpsertConnectionAsync(XeroConnection connection);
    Task UpdateConnectionAsync(XeroConnection connection);
    Task<ChartOfAccount?> GetAccountByXeroIdAsync(string organizationId, string xeroAccountId);
    // Every account this org has that came from Xero (custom ones excluded).
    Task<List<ChartOfAccount>> GetXeroSourcedAccountsAsync(string organizationId);
    Task AddAccountAsync(ChartOfAccount account);
    Task UpdateAccountAsync(ChartOfAccount account);
    Task<int> ArchiveManualProjectsAsync(string organizationId);
    // The starter claim item types drop out while Xero owns the chart, and
    // come back on disconnect (see DefaultClaimItemTypes).
    Task<int> ArchiveDefaultAccountsAsync(string organizationId) => Task.FromResult(0);
    Task<int> RestoreAccountsArchivedByXeroConnectAsync(string organizationId) => Task.FromResult(0);
    Task<int> RestoreProjectsArchivedByXeroConnectAsync(string organizationId);
    Task<Project?> GetProjectByXeroIdAsync(string organizationId, string xeroProjectId);
    Task<Project?> GetProjectByTrackingOptionAsync(string organizationId, string trackingOptionId);
    Task AddProjectAsync(Project project);
    Task UpdateProjectAsync(Project project);
}
