using AltomateHR.Api.Modules.Xero.Dtos;

namespace AltomateHR.Api.Modules.Xero;

public interface IXeroClient
{
    string BuildAuthorizationUrl(string state);
    Task<XeroTokenResponse> ExchangeCodeAsync(string code);
    Task<XeroTokenResponse> RefreshTokenAsync(string refreshToken);
    Task<List<XeroTenantResponse>> GetTenantsAsync(string accessToken);
    Task<List<XeroAccountResponse>> GetAccountsAsync(string accessToken, string tenantId);
    Task<List<XeroProjectResponse>> GetProjectsAsync(string accessToken, string tenantId);

    // Raw bytes of a file in Xero Files. Used to PROXY attachments to the
    // browser so the OAuth token never leaves the server. Null when Xero
    // reports the file is missing.
    Task<XeroFileContent?> GetFileContentAsync(string accessToken, string tenantId, string fileId);

    // Creates an accounts-payable bill (Xero calls it an ACCPAY Invoice).
    // Returns the bill's id and human reference so the caller can link to it.
    Task<XeroBillResponse> CreateBillAsync(string accessToken, string tenantId, XeroBillRequest bill);

    // Records money that already left a company bank account (Xero: a SPEND
    // bank transaction). The company-paid counterpart to CreateBillAsync.
    Task<XeroSpendResponse> CreateSpendAsync(string accessToken, string tenantId, XeroSpendRequest spend);
}

public sealed record XeroFileContent(byte[] Content, string ContentType, string FileName);

public sealed record XeroTokenResponse(
    string AccessToken,
    string RefreshToken,
    int ExpiresIn,
    string TokenType,
    string Scope);

public sealed record XeroTenantResponse(
    string Id,
    string TenantId,
    string TenantName,
    string? TenantType);

public sealed record XeroAccountResponse(
    string AccountId,
    string Code,
    string Name,
    string Type,
    string Status,
    bool? EnablePaymentsToAccount);

public sealed record XeroProjectResponse(
    string ProjectId,
    string Name,
    string Status);
