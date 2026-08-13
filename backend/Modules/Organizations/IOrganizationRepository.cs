using AltomateHR.Api.Modules.Organizations.Entities;

namespace AltomateHR.Api.Modules.Organizations;

public interface IOrganizationRepository
{
    Task<Organization?> GetByIdAsync(string id);
    Task<Organization?> GetFirstAsync();
    Task AddAsync(Organization organization);
    Task UpdateAsync(Organization organization);
    Task<bool> AnyAsync();
}
