using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.DocumentExpiry.Entities;
using AltomateHR.Api.Modules.Employees.Dtos;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.DocumentExpiry;

public class DocumentExpiryRepository : IDocumentExpiryRepository
{
    private readonly AppDbContext _db;

    public DocumentExpiryRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<(string Id, string Name)>> GetCompaniesAsync()
    {
        var withDates = _db.EmployeeProfiles.IgnoreQueryFilters()
            .Where(p => !p.IsArchived && (p.WorkPermitExpiry != null || p.PassportExpiry != null))
            .Select(p => p.OrganizationId);

        var rows = await _db.Organizations.AsNoTracking()
            .Where(o => o.DeactivatedAt == null && withDates.Contains(o.Id))
            .OrderBy(o => o.Name)
            .Select(o => new { o.Id, o.Name })
            .ToListAsync();
        return [.. rows.Select(r => (r.Id, r.Name))];
    }

    public async Task<IReadOnlyList<IdentityDocumentsDto>> GetDocumentsAsync(string organizationId)
    {
        var profiles = await _db.EmployeeProfiles.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.OrganizationId == organizationId
                        && (p.WorkPermitExpiry != null || p.PassportExpiry != null))
            .ToListAsync();
        return [.. profiles.Select(IdentityDocumentsDto.From)];
    }

    public async Task<IReadOnlyList<DocumentExpiryReminder>> GetSentAsync(string organizationId) =>
        await _db.DocumentExpiryReminders.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.OrganizationId == organizationId)
            .ToListAsync();

    public async Task AddSentAsync(IReadOnlyList<DocumentExpiryReminder> reminders)
    {
        if (reminders.Count == 0) return;
        _db.DocumentExpiryReminders.AddRange(reminders);
        await _db.SaveChangesAsync();
    }
}
