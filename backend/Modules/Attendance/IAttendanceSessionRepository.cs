using AltomateHR.Api.Modules.Attendance.Entities;

namespace AltomateHR.Api.Modules.Attendance;

public interface IAttendanceSessionRepository
{
    Task<AttendanceSession?> GetOpenForRecordAsync(string attendanceRecordId);
    Task<AttendanceSession?> GetByIdAsync(string id);

    // Org-agnostic — used by the auto-clock-out sweep, which runs outside any
    // request context so the tenant filter is a no-op (matches DbSeeder).
    Task<List<AttendanceSession>> GetOpenStartedBeforeAsync(DateTime cutoff, int limit);

    Task<AttendanceSession> AddAsync(AttendanceSession session);
    Task UpdateAsync(AttendanceSession session);
}
