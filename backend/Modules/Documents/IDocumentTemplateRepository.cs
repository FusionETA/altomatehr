using AltomateHR.Api.Modules.Documents.Entities;

namespace AltomateHR.Api.Modules.Documents;

// Data access for the letter template library. Tenant-scoped: the global
// query filter keeps every read and write inside the caller's company.
public interface IDocumentTemplateRepository
{
    Task<List<DocumentTemplate>> GetAllAsync();
    Task<DocumentTemplate?> GetByIdAsync(string id);

    // The sample keys this company already has, for "Add sample templates".
    Task<List<string>> GetSampleKeysAsync();

    Task<DocumentTemplate> AddAsync(DocumentTemplate template);
    Task AddRangeAsync(IEnumerable<DocumentTemplate> templates);
    Task UpdateAsync(DocumentTemplate template);
    Task DeleteAsync(DocumentTemplate template);
}
