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

    public Task<List<AttendanceRecord>> GetByIdsAsync(IEnumerable<string> ids) =>
        _db.AttendanceRecords.Where(r => ids.Contains(r.Id)).ToListAsync();

    public Task<List<AttendanceRecord>> GetForEmployeesOnDateAsync(
        IEnumerable<string> employeeIds, DateTime date) =>
        _db.AttendanceRecords
            .Where(r => employeeIds.Contains(r.EmployeeId) && r.Date == date)
            .ToListAsync();

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

    public Task<List<AttendanceRecord>> GetWithPhotosAsync() =>
        _db.AttendanceRecords
            .Where(r => r.ClockInPhotoUrl != null || r.ClockOutPhotoUrl != null)
            .ToListAsync();

    public Task<List<AttendanceRecord>> GetWithPhotosInRangeAsync(DateTime from, DateTime to) =>
        _db.AttendanceRecords
            .Where(r => r.Date >= from && r.Date <= to
                && (r.ClockInPhotoUrl != null || r.ClockOutPhotoUrl != null))
            .ToListAsync();

    public Task<List<AttendanceRecord>> GetOpenRecordsAsync() =>
        _db.AttendanceRecords.Where(r => r.TimeIn != null && r.TimeOut == null).ToListAsync();

    public Task<AttendanceRecord?> GetOpenForEmployeeAsync(string employeeId) =>
        _db.AttendanceRecords
            .Where(r => r.EmployeeId == employeeId && r.TimeIn != null && r.TimeOut == null)
            .OrderByDescending(r => r.Date)
            .FirstOrDefaultAsync();

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
