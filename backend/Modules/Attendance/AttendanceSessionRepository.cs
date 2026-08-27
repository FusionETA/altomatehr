using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Attendance.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Attendance;

public class AttendanceSessionRepository : IAttendanceSessionRepository
{
    private readonly AppDbContext _db;

    public AttendanceSessionRepository(AppDbContext db) => _db = db;

    // All queries are auto-scoped to the current org by the global query filter.
    public Task<AttendanceSession?> GetOpenForRecordAsync(string attendanceRecordId) =>
        _db.AttendanceSessions
            .Where(s => s.AttendanceRecordId == attendanceRecordId && s.EndedAt == null)
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync();

    public Task<AttendanceSession?> GetByIdAsync(string id) =>
        _db.AttendanceSessions.FirstOrDefaultAsync(s => s.Id == id);

    public Task<List<AttendanceSession>> GetOpenStartedBeforeAsync(DateTime cutoff, int limit) =>
        _db.AttendanceSessions
            .Where(s => s.EndedAt == null && s.StartedAt <= cutoff)
            .OrderBy(s => s.StartedAt)
            .Take(limit)
            .ToListAsync();

    public async Task<AttendanceSession> AddAsync(AttendanceSession session)
    {
        _db.AttendanceSessions.Add(session);
        await _db.SaveChangesAsync();   // OrganizationId auto-stamped here
        return session;
    }

    public async Task UpdateAsync(AttendanceSession session)
    {
        _db.AttendanceSessions.Update(session);
        await _db.SaveChangesAsync();
    }
}
