using AltomateHR.Api.Modules.Organizations.Entities;

namespace AltomateHR.Api.Modules.Organizations;

// Data access for saved signatures. Tenant-scoped: the global query filter
// keeps every read and write inside the caller's company, so another company's
// id simply isn't found. The "active" reads skip removed signatures.
//
// Every write is a single guarded statement that sets only the columns it
// changes — never "load, change, save the whole row" — so two admins working
// at once can't leave two defaults, or bring a removed signature back.
public interface IOrganizationSignatureRepository
{
    // Active only: the default first, then by name. Not tracked.
    Task<List<OrganizationSignature>> GetActiveAsync();

    // Null when not found in this company, or removed. Not tracked.
    Task<OrganizationSignature?> GetActiveByIdAsync(string id);

    // Inserts a new signature as it is (callers add it with IsDefault=false
    // and then call MakeDefaultAsync).
    Task AddAsync(OrganizationSignature signature);

    // One statement over the company's active signatures: this one becomes
    // the default and every other loses the flag. Concurrent calls end with
    // exactly one default (the last to run). False when the signature isn't
    // active (removed meanwhile) — then no row is the default.
    Task<bool> MakeDefaultAsync(string id);

    // Clears this signature's default flag. False when it isn't active.
    Task<bool> ClearDefaultAsync(string id);

    // Sets the name and position of an ACTIVE signature only. False (→ 404)
    // when it was removed meanwhile; RemovedAt is never written here.
    Task<bool> UpdateDetailsAsync(string id, string name, string position);

    // Soft remove: RemovedAt and IsDefault=false, only while still active.
    // False when already removed (by someone else) or not found.
    Task<bool> SoftRemoveAsync(string id, DateTime removedAt);
}
