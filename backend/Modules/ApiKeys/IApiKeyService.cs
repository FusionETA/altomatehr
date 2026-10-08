using AltomateHR.Api.Modules.ApiKeys.Dtos;

namespace AltomateHR.Api.Modules.ApiKeys;

public interface IApiKeyService
{
    // Create a key for the caller's active org. Returns the raw token ONCE.
    Task<CreatedApiKeyDto> CreateAsync(CreateApiKeyDto dto);

    Task<IReadOnlyList<ApiKeyDto>> GetAllAsync();

    // Soft-revoke (Active=false). True if the key exists in this org, false if not.
    Task<bool> RevokeAsync(string id);

    // The current org is connected to ABPay: it has an ACTIVE key whose name
    // contains "ABPay" (any case; "AB Pay" / "AB-Pay" count too). ABPay calls
    // each company with that company's own wp_live_ key, which the Owner
    // creates and labels — so the key IS the connection. Gates the AB Pay
    // timesheet export.
    Task<bool> HasAbPayIntegrationAsync();
}
