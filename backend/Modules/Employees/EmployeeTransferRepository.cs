using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Employees.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Employees;

public class EmployeeTransferRepository : IEmployeeTransferRepository
{
    // "Open" = PENDING or FAILED (awaiting retry). Spelled out as two
    // comparisons: an array .Contains here binds to the span overload, which
    // EF cannot translate.
    private readonly AppDbContext _db;

    public EmployeeTransferRepository(AppDbContext db) => _db = db;

    public Task<EmployeeTransfer?> GetOpenForUserAsync(string userId) =>
        _db.EmployeeTransfers
            .Where(t => t.UserId == userId && (t.Status == EmployeeTransferStatus.PENDING || t.Status == EmployeeTransferStatus.FAILED))
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync();

    public Task<List<EmployeeTransfer>> GetOpenForCurrentOrgAsync() =>
        _db.EmployeeTransfers.Where(t => (t.Status == EmployeeTransferStatus.PENDING || t.Status == EmployeeTransferStatus.FAILED)).ToListAsync();

    public Task<EmployeeTransfer?> GetByIdAsync(string id) =>
        _db.EmployeeTransfers.FirstOrDefaultAsync(t => t.Id == id);

    public Task<EmployeeTransfer?> GetByIdAnyOrgAsync(string id) =>
        _db.EmployeeTransfers.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == id);

    public Task<List<string>> GetDueIdsAsync(DateTime today, int max) =>
        _db.EmployeeTransfers.IgnoreQueryFilters()
            .Where(t => (t.Status == EmployeeTransferStatus.PENDING || t.Status == EmployeeTransferStatus.FAILED) && t.EffectiveDate <= today)
            .OrderBy(t => t.EffectiveDate)
            .Take(max)
            .Select(t => t.Id)
            .ToListAsync();

    public async Task AddAsync(EmployeeTransfer transfer)
    {
        transfer.CreatedAt = DateTime.UtcNow;
        _db.EmployeeTransfers.Add(transfer);   // StampTenant sets OrganizationId = the source (active) org
        await _db.SaveChangesAsync();
    }

    public Task UpdateAsync(EmployeeTransfer transfer) => _db.SaveChangesAsync();

    public async Task CommitAsync(
        IEnumerable<OrganizationMembership> newMemberships, IEnumerable<EmployeeProfile> newProfiles)
    {
        var now = DateTime.UtcNow;
        foreach (var m in newMemberships)
        {
            m.CreatedAt = now;
            m.UpdatedAt = now;
            _db.OrganizationMemberships.Add(m);
        }
        foreach (var p in newProfiles)
        {
            p.CreatedAt = now;
            p.UpdatedAt = now;
            _db.EmployeeProfiles.Add(p);
        }
        await _db.SaveChangesAsync();
    }

    public async Task CommitExecutionAsync(
        EmployeeTransfer transfer,
        bool isNewTransfer,
        IEnumerable<OrganizationMembership> newMemberships,
        IEnumerable<EmployeeProfile> newProfiles)
    {
        var now = DateTime.UtcNow;

        if (isNewTransfer)
        {
            transfer.CreatedAt = now;
            _db.EmployeeTransfers.Add(transfer);
        }

        foreach (var m in newMemberships)
        {
            m.CreatedAt = now;
            m.UpdatedAt = now;
            _db.OrganizationMemberships.Add(m);
        }

        foreach (var p in newProfiles)
        {
            p.CreatedAt = now;
            p.UpdatedAt = now;
            _db.EmployeeProfiles.Add(p);
        }

        // One SaveChanges is one transaction.
        await _db.SaveChangesAsync();
    }
}
