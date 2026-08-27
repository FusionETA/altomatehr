using AltomateHR.Api.Modules.Attendance.Entities;

namespace AltomateHR.Api.Modules.Attendance;

public interface IAttendanceApprovalRequestRepository
{
    Task<AttendanceApprovalRequest?> GetByIdAsync(string id);
    Task<List<AttendanceApprovalRequest>> GetByIdsAsync(IEnumerable<string> ids);
    Task<List<AttendanceApprovalRequest>> GetOpenByKindsAsync(IEnumerable<AttendanceApprovalKind> kinds);
    Task<List<AttendanceApprovalRequest>> GetByRecordIdsAsync(IEnumerable<string> recordIds);
    Task<List<AttendanceApprovalRequest>> GetByBreakIdsAsync(IEnumerable<string> breakIds);
    Task<List<AttendanceApprovalRequest>> GetForAuditAsync(
        string? employeeId, DateTime? from, DateTime? to, int limit = 500);
    Task<AttendanceApprovalRequest> AddAsync(AttendanceApprovalRequest request);
    Task UpdateAsync(AttendanceApprovalRequest request);
    Task UpdateRangeAsync(IEnumerable<AttendanceApprovalRequest> requests);
}
