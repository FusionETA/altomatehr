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

    // Fetch a file from Xero Files for the CURRENT org's connection. Other
    // modules go through here rather than IXeroClient so connection lookup and
    // token refresh stay in one place. Null = no connection, or no such file.
    Task<XeroFileContent?> GetFileContentAsync(string fileId);

    // Push a bill for the CURRENT org. Throws XeroConnectionException when the
    // org has no Xero connection — callers surface that as "connect Xero first"
    // rather than as a claim-level failure, because it is neither.
    Task<XeroBillResponse> CreateBillAsync(XeroBillRequest bill);

    // Whether the current org could sync at all, so a UI can say "connect Xero"
    // instead of offering a button that can only fail.
    Task<bool> IsConnectedAsync();
}
