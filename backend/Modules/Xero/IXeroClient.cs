namespace AltomateHR.Api.Modules.Xero;

public interface IXeroClient
{
    string BuildAuthorizationUrl(string state);
    Task<XeroTokenResponse> ExchangeCodeAsync(string code);
    Task<XeroTokenResponse> RefreshTokenAsync(string refreshToken);
    Task<List<XeroTenantResponse>> GetTenantsAsync(string accessToken);
    Task<List<XeroAccountResponse>> GetAccountsAsync(string accessToken, string tenantId);
    Task<List<XeroProjectResponse>> GetProjectsAsync(string accessToken, string tenantId);
}

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
