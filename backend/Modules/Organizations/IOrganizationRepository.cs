using AltomateHR.Api.Modules.Organizations.Entities;

namespace AltomateHR.Api.Modules.Organizations;

public interface IOrganizationRepository
{
    Task<Organization?> GetByIdAsync(string id);
    Task<Organization?> GetFirstAsync();

    // Every organisation. Not tenant-filtered — Organization itself never is,
    // since the filter exists to scope rows TO an org.
    Task<List<Organization>> GetAllAsync();
    Task AddAsync(Organization organization);
    Task UpdateAsync(Organization organization);
    Task<bool> AnyAsync();
}
