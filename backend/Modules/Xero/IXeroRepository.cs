using AltomateHR.Api.Modules.Accounts.Entities;
using AltomateHR.Api.Modules.Projects.Entities;
using AltomateHR.Api.Modules.Xero.Entities;

namespace AltomateHR.Api.Modules.Xero;

public interface IXeroRepository
{
    Task<XeroOAuthState> AddStateAsync(XeroOAuthState state);
    Task<XeroOAuthState?> GetStateAsync(string state);
    Task UpdateStateAsync(XeroOAuthState state);
    Task<XeroConnection?> GetConnectionAsync(string organizationId);
    Task<XeroConnection> UpsertConnectionAsync(XeroConnection connection);
    Task UpdateConnectionAsync(XeroConnection connection);
    Task<ChartOfAccount?> GetAccountByXeroIdAsync(string organizationId, string xeroAccountId);
    Task AddAccountAsync(ChartOfAccount account);
    Task UpdateAccountAsync(ChartOfAccount account);
    Task<Project?> GetProjectByXeroIdAsync(string organizationId, string xeroProjectId);
    Task AddProjectAsync(Project project);
    Task UpdateProjectAsync(Project project);
}
