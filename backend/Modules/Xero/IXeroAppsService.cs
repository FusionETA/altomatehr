using AltomateHR.Api.Modules.Xero.Dtos;

namespace AltomateHR.Api.Modules.Xero;

// Superadmin only (XeroAppsController): the Xero apps companies connect
// through, and which one each owner's companies use.
public interface IXeroAppsService
{
    Task<XeroAppsOverviewDto> GetOverviewAsync();

    // ArgumentException for a bad name, client id or missing secret.
    Task<XeroAppDto> AddAppAsync(SaveXeroAppDto dto);
    Task<XeroAppDto?> UpdateAppAsync(string id, SaveXeroAppDto dto);

    // Turns the original app (server settings) into a normal, editable app
    // under `name`. ArgumentException when it isn't set up or already moved.
    Task<XeroAppDto> AdoptOriginalAppAsync(string name);

    // Refused while a company is connected through it or an owner is set to it.
    Task<(bool NotFound, string? Error)> DeleteAppAsync(string id);

    // ArgumentException for someone who owns no company, or an unknown app.
    Task SetOwnerChoiceAsync(string ownerUserId, SetXeroOwnerChoiceDto dto);

    // A co-owned company: which owner's choice it follows.
    Task<(bool NotFound, string? Error)> SetCompanyOwnerAsync(string organizationId, string ownerUserId);
}
