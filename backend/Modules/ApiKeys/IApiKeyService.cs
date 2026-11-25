using AltomateHR.Api.Modules.ApiKeys.Dtos;

namespace AltomateHR.Api.Modules.ApiKeys;

public interface IApiKeyService
{
    // Create a key for the caller's active org. Returns the raw token ONCE.
    Task<CreatedApiKeyDto> CreateAsync(CreateApiKeyDto dto);

    Task<IReadOnlyList<ApiKeyDto>> GetAllAsync();

    // Change a key's label and scopes in place — the token stays the same, and
    // the new scopes apply from its next request. Null if the key isn't in this
    // org (or is an assistant's FAH key, which isn't managed here). Throws
    // ArgumentException for an unknown scope, InvalidOperationException for a
    // revoked key.
    Task<ApiKeyDto?> UpdateAsync(string id, UpdateApiKeyDto dto);

    // Soft-revoke (Active=false). True if the key exists in this org, false if not.
    Task<bool> RevokeAsync(string id);

    // The current org is connected to ABPay: it has an ACTIVE key whose name
    // contains "ABPay" (any case; "AB Pay" / "AB-Pay" count too). ABPay calls
    // each company with that company's own wp_live_ key, which the Owner
    // creates and labels — so the key IS the connection. Gates the AB Pay
    // timesheet export.
    Task<bool> HasAbPayIntegrationAsync();
}
