using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Documents.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Documents;

public class DocumentTemplateRepository : IDocumentTemplateRepository
{
    private readonly AppDbContext _db;

    public DocumentTemplateRepository(AppDbContext db) => _db = db;

    // All queries are auto-scoped to the current org by the global query filter.
    public Task<List<DocumentTemplate>> GetAllAsync() =>
        _db.DocumentTemplates.OrderBy(t => t.Category).ThenBy(t => t.Name).ToListAsync();

    public Task<DocumentTemplate?> GetByIdAsync(string id) =>
        _db.DocumentTemplates.FirstOrDefaultAsync(t => t.Id == id);

    public Task<List<string>> GetSampleKeysAsync() =>
        _db.DocumentTemplates.Where(t => t.SampleKey != null).Select(t => t.SampleKey!).ToListAsync();

    public async Task<DocumentTemplate> AddAsync(DocumentTemplate template)
    {
        _db.DocumentTemplates.Add(template);
        await _db.SaveChangesAsync();   // OrganizationId auto-stamped here
        return template;
    }

    public async Task AddRangeAsync(IEnumerable<DocumentTemplate> templates)
    {
        _db.DocumentTemplates.AddRange(templates);
        await _db.SaveChangesAsync();
    }

    public async Task UpdateAsync(DocumentTemplate template)
    {
        _db.DocumentTemplates.Update(template);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(DocumentTemplate template)
    {
        _db.DocumentTemplates.Remove(template);
        await _db.SaveChangesAsync();
    }
}
