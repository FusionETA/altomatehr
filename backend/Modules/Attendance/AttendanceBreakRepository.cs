using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Attendance.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Attendance;

public class AttendanceBreakRepository : IAttendanceBreakRepository
{
    private readonly AppDbContext _db;

    public AttendanceBreakRepository(AppDbContext db) => _db = db;

    // All queries are auto-scoped to the current org by the global query filter.
    public Task<AttendanceBreak?> GetOpenForSessionAsync(string attendanceSessionId) =>
        _db.AttendanceBreaks
            .Where(b => b.AttendanceSessionId == attendanceSessionId && b.EndedAt == null)
            .OrderByDescending(b => b.StartedAt)
            .FirstOrDefaultAsync();

    public Task<AttendanceBreak?> GetByIdAsync(string id) =>
        _db.AttendanceBreaks.FirstOrDefaultAsync(b => b.Id == id);

    public Task<List<AttendanceBreak>> GetByRecordAsync(string attendanceRecordId) =>
        _db.AttendanceBreaks
            .Where(b => b.AttendanceRecordId == attendanceRecordId)
            .OrderBy(b => b.StartedAt)
            .ToListAsync();

    public Task<List<AttendanceBreak>> GetPendingAsync() =>
        _db.AttendanceBreaks
            .Where(b => b.ApprovalStatus == Entities.AttendanceApprovalStatus.PENDING)
            .ToListAsync();

    public async Task<AttendanceBreak> AddAsync(AttendanceBreak brk)
    {
        _db.AttendanceBreaks.Add(brk);
        await _db.SaveChangesAsync();   // OrganizationId auto-stamped here
        return brk;
    }

    public async Task UpdateAsync(AttendanceBreak brk)
    {
        _db.AttendanceBreaks.Update(brk);
        await _db.SaveChangesAsync();
    }
}
