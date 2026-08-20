using AltomateHR.Api.Modules.Xero.Dtos;

namespace AltomateHR.Api.Modules.Xero;

public interface IXeroService
{
    Task<XeroConnectUrlDto> CreateConnectUrlAsync(string? returnUrl);
    Task<string> CompleteCallbackAsync(string code, string state);
    Task<XeroStatusDto> GetStatusAsync();
    Task DisconnectAsync();
    Task<XeroSyncAccountsResultDto> SyncAccountsAsync();
    Task<XeroSyncProjectsResultDto> SyncProjectsAsync();
}
