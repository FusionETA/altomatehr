using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Attendance.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Attendance;

public class AttendanceApprovalRequestRepository : IAttendanceApprovalRequestRepository
{
    private readonly AppDbContext _db;

    public AttendanceApprovalRequestRepository(AppDbContext db) => _db = db;

    // All queries are auto-scoped to the current org by the global query filter.
    public Task<AttendanceApprovalRequest?> GetByIdAsync(string id) =>
        _db.AttendanceApprovalRequests.FirstOrDefaultAsync(a => a.Id == id);

    public Task<List<AttendanceApprovalRequest>> GetByIdsAsync(IEnumerable<string> ids) =>
        _db.AttendanceApprovalRequests.Where(a => ids.Contains(a.Id)).ToListAsync();

    public Task<List<AttendanceApprovalRequest>> GetOpenByKindsAsync(IEnumerable<AttendanceApprovalKind> kinds) =>
        _db.AttendanceApprovalRequests
            .Where(a => kinds.Contains(a.Kind) && a.ApprovalStatus == AttendanceApprovalStatus.PENDING)
            .ToListAsync();

    public Task<List<AttendanceApprovalRequest>> GetByRecordIdsAsync(IEnumerable<string> recordIds) =>
        _db.AttendanceApprovalRequests
            .Where(a => recordIds.Contains(a.AttendanceRecordId))
            .OrderBy(a => a.SubmittedAt)
            .ToListAsync();

    public Task<List<AttendanceApprovalRequest>> GetByBreakIdsAsync(IEnumerable<string> breakIds) =>
        _db.AttendanceApprovalRequests
            .Where(a => a.AttendanceBreakId != null && breakIds.Contains(a.AttendanceBreakId))
            .OrderBy(a => a.SubmittedAt)
            .ToListAsync();

    public Task<List<AttendanceApprovalRequest>> GetForAuditAsync(
        string? employeeId, DateTime? from, DateTime? to, int limit = 500)
    {
        var query = _db.AttendanceApprovalRequests.AsQueryable();
        if (!string.IsNullOrEmpty(employeeId)) query = query.Where(a => a.EmployeeId == employeeId);
        if (from is not null) query = query.Where(a => a.SubmittedAt >= from);
        if (to is not null) query = query.Where(a => a.SubmittedAt <= to);
        return query.OrderByDescending(a => a.SubmittedAt).Take(limit).ToListAsync();
    }

    public async Task<AttendanceApprovalRequest> AddAsync(AttendanceApprovalRequest request)
    {
        _db.AttendanceApprovalRequests.Add(request);
        await _db.SaveChangesAsync();   // OrganizationId auto-stamped here
        return request;
    }

    public async Task UpdateAsync(AttendanceApprovalRequest request)
    {
        _db.AttendanceApprovalRequests.Update(request);
        await _db.SaveChangesAsync();
    }

    public async Task UpdateRangeAsync(IEnumerable<AttendanceApprovalRequest> requests)
    {
        _db.AttendanceApprovalRequests.UpdateRange(requests);
        await _db.SaveChangesAsync();
    }
}
