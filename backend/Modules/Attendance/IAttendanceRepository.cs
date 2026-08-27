using AltomateHR.Api.Modules.Attendance.Entities;

namespace AltomateHR.Api.Modules.Attendance;

public interface IAttendanceRepository
{
    Task<AttendanceRecord?> GetForEmployeeOnDateAsync(string employeeId, DateTime date);
    Task<AttendanceRecord?> GetByIdAsync(string id);
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

    Task<AttendanceRecord> AddAsync(AttendanceRecord record);
    Task UpdateAsync(AttendanceRecord record);
}
