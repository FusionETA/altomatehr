using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Attendance.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Attendance;

public class AttendanceRepository : IAttendanceRepository
{
    private readonly AppDbContext _db;

    public AttendanceRepository(AppDbContext db) => _db = db;

    // All queries are auto-scoped to the current org by the global query filter.
    public Task<AttendanceRecord?> GetForEmployeeOnDateAsync(string employeeId, DateTime date) =>
        _db.AttendanceRecords.FirstOrDefaultAsync(r => r.EmployeeId == employeeId && r.Date == date);

    public Task<AttendanceRecord?> GetByIdAsync(string id) =>
        _db.AttendanceRecords.FirstOrDefaultAsync(r => r.Id == id);

    public Task<AttendanceRecord?> GetByPhotoUrlAsync(string photoUrl) =>
        _db.AttendanceRecords.FirstOrDefaultAsync(
            r => r.ClockInPhotoUrl == photoUrl || r.ClockOutPhotoUrl == photoUrl);

    public Task<List<AttendanceRecord>> GetByEmployeeAsync(string employeeId) =>
        _db.AttendanceRecords
            .Where(r => r.EmployeeId == employeeId)
            .OrderByDescending(r => r.Date)
            .ToListAsync();

    public Task<List<AttendanceRecord>> GetAllAsync() =>
        _db.AttendanceRecords.OrderByDescending(r => r.Date).ToListAsync();

    public async Task<AttendanceRecord> AddAsync(AttendanceRecord record)
    {
        _db.AttendanceRecords.Add(record);
        await _db.SaveChangesAsync();   // OrganizationId auto-stamped here
        return record;
    }

    public async Task UpdateAsync(AttendanceRecord record)
    {
        _db.AttendanceRecords.Update(record);
        await _db.SaveChangesAsync();
    }
}
