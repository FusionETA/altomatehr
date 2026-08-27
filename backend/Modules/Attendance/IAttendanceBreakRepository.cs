using AltomateHR.Api.Modules.Attendance.Entities;

namespace AltomateHR.Api.Modules.Attendance;

public interface IAttendanceBreakRepository
{
    Task<AttendanceBreak?> GetOpenForSessionAsync(string attendanceSessionId);
    Task<AttendanceBreak?> GetByIdAsync(string id);
    Task<List<AttendanceBreak>> GetByRecordAsync(string attendanceRecordId);
    Task<AttendanceBreak> AddAsync(AttendanceBreak brk);
    Task UpdateAsync(AttendanceBreak brk);
}
