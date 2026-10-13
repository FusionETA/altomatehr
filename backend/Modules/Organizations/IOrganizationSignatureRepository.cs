using AltomateHR.Api.Modules.Organizations.Entities;

namespace AltomateHR.Api.Modules.Organizations;

// Data access for saved signatures. Tenant-scoped: the global query filter
// keeps every read inside the caller's company, so another company's id simply
// isn't found. The "active" reads skip removed signatures.
public interface IOrganizationSignatureRepository
{
    // Active only: the default first, then by name.
    Task<List<OrganizationSignature>> GetActiveAsync();

    // Null when not found in this company, or removed.
    Task<OrganizationSignature?> GetActiveByIdAsync(string id);

    // Adds a signature, saving changes to `alsoUpdate` (the previous default
    // losing its flag) in the same save.
    Task AddAsync(OrganizationSignature signature, params OrganizationSignature[] alsoUpdate);

    // Saves changes to one or more signatures in one go (e.g. moving the default).
    Task UpdateAsync(params OrganizationSignature[] signatures);
}
