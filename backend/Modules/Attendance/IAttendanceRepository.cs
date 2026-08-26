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
    Task<AttendanceRecord> AddAsync(AttendanceRecord record);
    Task UpdateAsync(AttendanceRecord record);
}
