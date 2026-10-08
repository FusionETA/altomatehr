using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Documents.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Documents;

public class GeneratedDocumentRepository : IGeneratedDocumentRepository
{
    private readonly AppDbContext _db;

    public GeneratedDocumentRepository(AppDbContext db) => _db = db;

    public Task<List<GeneratedDocument>> ListAsync(string? employeeUserId, int max)
    {
        var query = _db.GeneratedDocuments.AsQueryable();
        if (!string.IsNullOrEmpty(employeeUserId))
            query = query.Where(d => d.EmployeeUserId == employeeUserId);
        return query.OrderByDescending(d => d.CreatedAt).Take(max).ToListAsync();
    }

    public Task<GeneratedDocument?> GetByIdAsync(string id) =>
        _db.GeneratedDocuments.FirstOrDefaultAsync(d => d.Id == id);

    public async Task AddAsync(GeneratedDocument document)
    {
        _db.GeneratedDocuments.Add(document);
        await _db.SaveChangesAsync();   // OrganizationId auto-stamped here
    }

    public async Task DeleteAsync(GeneratedDocument document)
    {
        _db.GeneratedDocuments.Remove(document);
        await _db.SaveChangesAsync();
    }
}
