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
}
