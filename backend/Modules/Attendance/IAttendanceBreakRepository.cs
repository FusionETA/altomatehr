using AltomateHR.Api.Modules.Attendance.Entities;

namespace AltomateHR.Api.Modules.Attendance;

public interface IAttendanceBreakRepository
{
    Task<AttendanceBreak?> GetOpenForSessionAsync(string attendanceSessionId);
    Task<AttendanceBreak?> GetByIdAsync(string id);
    Task<List<AttendanceBreak>> GetByRecordAsync(string attendanceRecordId);

    // Bulk variant — the hours summary needs breaks for a whole date range, and
    // one query per record turns an org-wide summary into hundreds of them.
    Task<List<AttendanceBreak>> GetByRecordsAsync(IEnumerable<string> attendanceRecordIds);
    Task<AttendanceBreak> AddAsync(AttendanceBreak brk);
    Task UpdateAsync(AttendanceBreak brk);
}
