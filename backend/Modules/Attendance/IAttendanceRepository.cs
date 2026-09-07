using AltomateHR.Api.Modules.Attendance.Entities;

namespace AltomateHR.Api.Modules.Attendance;

public interface IAttendanceRepository
{
    Task<AttendanceRecord?> GetForEmployeeOnDateAsync(string employeeId, DateTime date);
    Task<AttendanceRecord?> GetByIdAsync(string id);
    Task<List<AttendanceRecord>> GetByIdsAsync(IEnumerable<string> ids);
    Task<List<AttendanceRecord>> GetForEmployeesOnDateAsync(IEnumerable<string> employeeIds, DateTime date);
    Task<AttendanceRecord?> GetByPhotoUrlAsync(string photoUrl);
    Task<List<AttendanceRecord>> GetByEmployeeAsync(string employeeId);
    Task<List<AttendanceRecord>> GetAllAsync();
    Task<List<AttendanceRecord>> GetWithPhotosAsync();
    Task<List<AttendanceRecord>> GetWithPhotosInRangeAsync(DateTime from, DateTime to);

    // Currently clocked in (TimeIn set, TimeOut not yet). Used by the
    // still-clocked-in warning — both the on-demand endpoint and the
    // background sweep, the latter running outside any request context so
    // the tenant filter is a no-op (matches DbSeeder).
    Task<List<AttendanceRecord>> GetOpenRecordsAsync();

    // This employee's unfinished session, whichever day it belongs to. A shift
    // left open yesterday still blocks clocking in today, so the lookup can't be
    // scoped to the current day the way the rest of the clock flow is.
    Task<AttendanceRecord?> GetOpenForEmployeeAsync(string employeeId);

    Task<AttendanceRecord> AddAsync(AttendanceRecord record);
    Task UpdateAsync(AttendanceRecord record);
}
