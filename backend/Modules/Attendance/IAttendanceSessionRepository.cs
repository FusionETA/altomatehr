using AltomateHR.Api.Modules.Attendance.Entities;

namespace AltomateHR.Api.Modules.Attendance;

public interface IAttendanceSessionRepository
{
    Task<AttendanceSession?> GetOpenForRecordAsync(string attendanceRecordId);
    Task<AttendanceSession?> GetByIdAsync(string id);
    Task<AttendanceSession> AddAsync(AttendanceSession session);
    Task UpdateAsync(AttendanceSession session);
}
