using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Leave.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Leave;

// All queries auto-scoped to the current org by the global query filter.
public class LeaveTypeRepository : ILeaveTypeRepository
{
    private readonly AppDbContext _db;

    public LeaveTypeRepository(AppDbContext db) => _db = db;

    public Task<List<LeaveType>> GetAllAsync() =>
        _db.LeaveTypes.OrderBy(t => t.Name).ToListAsync();

    public Task<LeaveType?> GetByIdAsync(string id) =>
        _db.LeaveTypes.FirstOrDefaultAsync(t => t.Id == id);

    public Task<LeaveType?> GetByCodeAsync(string code) =>
        _db.LeaveTypes.FirstOrDefaultAsync(t => t.Code == code);

    public async Task<LeaveType> AddAsync(LeaveType type)
    {
        _db.LeaveTypes.Add(type);
        await _db.SaveChangesAsync();   // OrganizationId auto-stamped here
        return type;
    }

    public async Task UpdateAsync(LeaveType type)
    {
        _db.LeaveTypes.Update(type);
        await _db.SaveChangesAsync();
    }
}

public class LeaveApplicationRepository : ILeaveApplicationRepository
{
    private readonly AppDbContext _db;

    public LeaveApplicationRepository(AppDbContext db) => _db = db;

    public Task<List<LeaveApplication>> GetAllAsync() =>
        _db.LeaveApplications.OrderByDescending(a => a.StartDate).ToListAsync();

    public Task<LeaveApplication?> GetByIdAsync(string id) =>
        _db.LeaveApplications.FirstOrDefaultAsync(a => a.Id == id);

    public Task<List<LeaveApplication>> GetByEmployeeAsync(string employeeId) =>
        _db.LeaveApplications
            .Where(a => a.EmployeeId == employeeId)
            .OrderByDescending(a => a.StartDate)
            .ToListAsync();

    public Task<LeaveApplication?> GetByXeroFileIdAsync(string xeroFileId) =>
        _db.LeaveApplications.FirstOrDefaultAsync(a => a.XeroFileId == xeroFileId);

    public async Task<LeaveApplication> AddAsync(LeaveApplication application)
    {
        _db.LeaveApplications.Add(application);
        await _db.SaveChangesAsync();
        return application;
    }

    public async Task UpdateAsync(LeaveApplication application)
    {
        _db.LeaveApplications.Update(application);
        await _db.SaveChangesAsync();
    }

    // On MySQL this is ONE guarded UPDATE … WHERE Id AND Status = 'PENDING'
    // AND CurrentStep = 0 (ExecuteUpdateAsync), atomic under InnoDB's row
    // locks, setting only the edited columns. The EF in-memory provider can't
    // run ExecuteUpdate, so there the same guard is checked against the stored
    // row first — the same result, minus the cross-request atomicity an
    // in-memory test can't exercise anyway. Same approach as
    // OrganizationSignatureRepository.
    public async Task<bool> SaveEditIfUntouchedAsync(LeaveApplication edited)
    {
        var entry = _db.Entry(edited);
        var untouched = _db.LeaveApplications.Where(a =>
            a.Id == edited.Id && a.Status == LeaveStatus.PENDING && a.CurrentStep == 0);

        if (_db.Database.IsRelational())
        {
            var affected = await untouched.ExecuteUpdateAsync(set => set
                .SetProperty(a => a.LeaveTypeId, edited.LeaveTypeId)
                .SetProperty(a => a.StartDate, edited.StartDate)
                .SetProperty(a => a.EndDate, edited.EndDate)
                .SetProperty(a => a.Duration, edited.Duration)
                .SetProperty(a => a.TotalDays, edited.TotalDays)
                .SetProperty(a => a.Reason, edited.Reason)
                .SetProperty(a => a.XeroFileId, edited.XeroFileId)
                .SetProperty(a => a.AttachmentUrl, edited.AttachmentUrl)
                .SetProperty(a => a.AttachmentName, edited.AttachmentName)
                .SetProperty(a => a.UpdatedAt, edited.UpdatedAt));

            // The statement already wrote the row (or refused to). Either way
            // the tracked copy must never be flushed by a later SaveChanges in
            // this request — that would be the unconditional write again — so
            // it is let go; the caller keeps the object for its response.
            entry.State = EntityState.Detached;
            return affected > 0;
        }

        if (!await untouched.AsNoTracking().AnyAsync())
        {
            entry.State = EntityState.Detached;
            return false;
        }

        if (entry.State == EntityState.Detached) _db.LeaveApplications.Attach(edited);
        foreach (var name in EditedColumns) entry.Property(name).IsModified = true;
        await _db.SaveChangesAsync();
        return true;
    }

    private static readonly string[] EditedColumns =
    [
        nameof(LeaveApplication.LeaveTypeId), nameof(LeaveApplication.StartDate),
        nameof(LeaveApplication.EndDate), nameof(LeaveApplication.Duration),
        nameof(LeaveApplication.TotalDays), nameof(LeaveApplication.Reason),
        nameof(LeaveApplication.XeroFileId), nameof(LeaveApplication.AttachmentUrl),
        nameof(LeaveApplication.AttachmentName), nameof(LeaveApplication.UpdatedAt),
    ];
}

public class LeaveEntitlementRepository : ILeaveEntitlementRepository
{
    private readonly AppDbContext _db;

    public LeaveEntitlementRepository(AppDbContext db) => _db = db;

    public Task<List<LeaveEntitlement>> GetByYearAsync(int year) =>
        _db.LeaveEntitlements.Where(e => e.Year == year).ToListAsync();

    public Task<List<LeaveEntitlement>> GetForEmployeeYearAsync(string employeeId, int year) =>
        _db.LeaveEntitlements
            .Where(e => e.EmployeeId == employeeId && e.Year == year)
            .ToListAsync();

    // Carried days that have lapsed and not yet been swept.
    public Task<List<LeaveEntitlement>> GetCarryExpiringAsync(DateTime asOf) =>
        _db.LeaveEntitlements
            .Where(e => !e.CarriedExpired
                        && e.CarriedDays > 0
                        && e.CarriedExpiresAt != null
                        && e.CarriedExpiresAt <= asOf)
            .ToListAsync();

    public async Task AddAsync(LeaveEntitlement entitlement) =>
        await _db.LeaveEntitlements.AddAsync(entitlement);

    // Tracked entities are flushed in one batch — the crons touch many rows.
    public Task SaveAsync() => _db.SaveChangesAsync();
}
