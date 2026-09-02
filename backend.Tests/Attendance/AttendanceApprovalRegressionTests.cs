using AltomateHR.Api.Tests.Common;
using AltomateHR.Api.Modules.Attendance;
using AltomateHR.Api.Modules.Attendance.Dtos;
using AltomateHR.Api.Modules.Attendance.Entities;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Policies;
using AltomateHR.Api.Modules.Policies.Dtos;
using AltomateHR.Api.Modules.Policies.Entities;
using AltomateHR.Api.Modules.Projects;
using AltomateHR.Api.Modules.Projects.Dtos;
using AltomateHR.Api.Tests.Claims;   // reuse FakeOrganizationService / FakeCurrentUser / FakeSupervisionService / FakeApprovalRouter

using AltomateHR.Api.Tests.Support;

namespace AltomateHR.Api.Tests.Attendance;

// Regression guard for the approval-overwrite bug.
//
// Before the per-event approval model, an AttendanceRecord carried a SINGLE
// ApprovalStatus slot. A supervisor could approve the clock-in, then the
// employee's later clock-out would silently reset that one slot back to
// PENDING — wiping a decision that had already been made.
//
// The fix models every event (CLOCK_IN, CLOCK_OUT, …) as its own
// AttendanceApprovalRequest row, so a clock-out only ADDS a CLOCK_OUT request
// and never touches the CLOCK_IN one. This test locks that behaviour in.
public class AttendanceApprovalRegressionTests
{
    [Fact]
    public async Task ClockOut_DoesNotOverwrite_AnAlreadyApprovedClockInRequest()
    {
        var now = DateTime.UtcNow;

        // --- Arrange: employee is clocked in, and their CLOCK_IN was already approved. ---
        // No project → EvaluateGeofenceAsync short-circuits, so clock-out needs no GPS proof.
        var record = new AttendanceRecord
        {
            Id = "rec-1",
            EmployeeId = "emp-1",
            Date = now.Date,
            TimeIn = now.AddHours(-8),
            TimeOut = null,
            ProjectId = null,
            Status = AttendanceStatus.CLOCKED_IN,
            CreatedAt = now.AddHours(-8),
            UpdatedAt = now.AddHours(-8),
        };
        var session = new AttendanceSession
        {
            Id = "sess-1",
            AttendanceRecordId = "rec-1",
            EmployeeId = "emp-1",
            StartedAt = now.AddHours(-8),
            EndedAt = null,
            CreatedAt = now.AddHours(-8),
            UpdatedAt = now.AddHours(-8),
        };
        var clockInApproval = new AttendanceApprovalRequest
        {
            Id = "req-in",
            EmployeeId = "emp-1",
            Kind = AttendanceApprovalKind.CLOCK_IN,
            AttendanceRecordId = "rec-1",
            AttendanceSessionId = "sess-1",
            ApprovalStatus = AttendanceApprovalStatus.APPROVED,   // supervisor already decided this
            ReviewerId = "usr-super",
            DecidedAt = now.AddHours(-7),
            EventAt = now.AddHours(-8),
            SubmittedAt = now.AddHours(-8),
            CreatedAt = now.AddHours(-8),
            UpdatedAt = now.AddHours(-7),
        };

        var approvals = new FakeAttendanceApprovalRequestRepository([clockInApproval]);
        var service = new AttendanceService(
            repo: new FakeAttendanceRepository([record]),
            sessions: new FakeAttendanceSessionRepository([session]),
            breaks: new FakeAttendanceBreakRepository(),
            approvalRequests: approvals,
            projects: new FakeProjectService(),
            organizations: new FakeOrganizationService(),
            currentUser: new FakeCurrentUser(),
            photos: new FakeAttendancePhotoStorage(),
            policies: new FakePolicyService(),
            supervision: new FakeSupervisionService(),
            router: new FakeApprovalRouter(),
            directory: TestDirectory.Over(new FakeOrganizationMembershipRepository()),
            realtime: new FakeRealtimeService(),
            employees: new FakeEmployeeDirectory(),
            hours: new FakeHoursSummaryService());

        // --- Act: employee clocks out. ---
        var result = await service.ClockOutAsync("emp-1", new ClockOutDto());

        // --- Assert ---
        Assert.True(result.Ok);

        // The bug: this decision would have been reset. The fix: it survives untouched.
        var clockIn = Assert.Single(approvals.Requests, r => r.Kind == AttendanceApprovalKind.CLOCK_IN);
        Assert.Equal("req-in", clockIn.Id);                                   // same row, not replaced
        Assert.Equal(AttendanceApprovalStatus.APPROVED, clockIn.ApprovalStatus);
        Assert.Equal("usr-super", clockIn.ReviewerId);                        // decision metadata intact

        // Clock-out records its own, independent request — still awaiting review.
        var clockOut = Assert.Single(approvals.Requests, r => r.Kind == AttendanceApprovalKind.CLOCK_OUT);
        Assert.Equal("rec-1", clockOut.AttendanceRecordId);
        Assert.Equal(AttendanceApprovalStatus.PENDING, clockOut.ApprovalStatus);
    }

    // ----------------------------------------------------------------------
    // Attendance-specific fakes. The shared collaborators (org / current-user /
    // supervision / router) are reused from ClaimsTestDoubles; the ones below
    // don't exist elsewhere. Repos are faithful in-memory lists; the two
    // services the clock-out path never reaches (photos, and — because the
    // record has no project — projects/policies) return safe defaults.
    // ----------------------------------------------------------------------

    private sealed class FakeAttendanceRepository : IAttendanceRepository
    {
        private readonly List<AttendanceRecord> _records;
        public FakeAttendanceRepository(IEnumerable<AttendanceRecord> records) => _records = records.ToList();

        // The service looks up "today's row" by employee; a single record per
        // employee in these tests makes the date match irrelevant.
        public Task<AttendanceRecord?> GetForEmployeeOnDateAsync(string employeeId, DateTime date) =>
            Task.FromResult(_records.FirstOrDefault(r => r.EmployeeId == employeeId));

        public Task<AttendanceRecord?> GetByIdAsync(string id) =>
            Task.FromResult(_records.FirstOrDefault(r => r.Id == id));
        public Task<AttendanceRecord?> GetByPhotoUrlAsync(string photoUrl) =>
            Task.FromResult(_records.FirstOrDefault(r => r.ClockInPhotoUrl == photoUrl || r.ClockOutPhotoUrl == photoUrl));
        public Task<List<AttendanceRecord>> GetByEmployeeAsync(string employeeId) =>
            Task.FromResult(_records.Where(r => r.EmployeeId == employeeId).ToList());
        public Task<List<AttendanceRecord>> GetAllAsync() => Task.FromResult(_records.ToList());
        public Task<List<AttendanceRecord>> GetWithPhotosAsync() =>
            Task.FromResult(_records.Where(r => r.ClockInPhotoUrl != null || r.ClockOutPhotoUrl != null).ToList());
        public Task<List<AttendanceRecord>> GetWithPhotosInRangeAsync(DateTime from, DateTime to) =>
            Task.FromResult(_records.Where(r => r.Date >= from && r.Date <= to).ToList());
        public Task<List<AttendanceRecord>> GetOpenRecordsAsync() =>
            Task.FromResult(_records.Where(r => r.TimeIn != null && r.TimeOut == null).ToList());
        public Task<AttendanceRecord> AddAsync(AttendanceRecord record) { _records.Add(record); return Task.FromResult(record); }
        public Task UpdateAsync(AttendanceRecord record) => Task.CompletedTask;   // service mutates in place
    }

    private sealed class FakeAttendanceSessionRepository : IAttendanceSessionRepository
    {
        private readonly List<AttendanceSession> _sessions;
        public FakeAttendanceSessionRepository(IEnumerable<AttendanceSession> sessions) => _sessions = sessions.ToList();

        public Task<AttendanceSession?> GetOpenForRecordAsync(string attendanceRecordId) =>
            Task.FromResult(_sessions.FirstOrDefault(s => s.AttendanceRecordId == attendanceRecordId && s.EndedAt == null));
        public Task<AttendanceSession?> GetByIdAsync(string id) =>
            Task.FromResult(_sessions.FirstOrDefault(s => s.Id == id));
        public Task<List<AttendanceSession>> GetOpenStartedBeforeAsync(DateTime cutoff, int limit) =>
            Task.FromResult(_sessions.Where(s => s.EndedAt == null && s.StartedAt < cutoff).Take(limit).ToList());
        public Task<AttendanceSession> AddAsync(AttendanceSession session) { _sessions.Add(session); return Task.FromResult(session); }
        public Task UpdateAsync(AttendanceSession session) => Task.CompletedTask;
    }

    private sealed class FakeAttendanceBreakRepository : IAttendanceBreakRepository
    {
        private readonly List<AttendanceBreak> _breaks = [];
        public Task<AttendanceBreak?> GetOpenForSessionAsync(string attendanceSessionId) =>
            Task.FromResult(_breaks.FirstOrDefault(b => b.AttendanceSessionId == attendanceSessionId && b.EndedAt == null));
        public Task<AttendanceBreak?> GetByIdAsync(string id) =>
            Task.FromResult(_breaks.FirstOrDefault(b => b.Id == id));
        public Task<List<AttendanceBreak>> GetByRecordAsync(string attendanceRecordId) =>
            Task.FromResult(_breaks.Where(b => b.AttendanceRecordId == attendanceRecordId).ToList());
        public Task<AttendanceBreak> AddAsync(AttendanceBreak brk) { _breaks.Add(brk); return Task.FromResult(brk); }
        public Task UpdateAsync(AttendanceBreak brk) => Task.CompletedTask;

        public Task<List<AttendanceBreak>> GetByRecordsAsync(IEnumerable<string> attendanceRecordIds) =>
            Task.FromResult(new List<AttendanceBreak>());
    }

    // The star of the test: an in-memory approval-request store whose contents
    // the assertions read back. `Requests` exposes the live list.
    private sealed class FakeAttendanceApprovalRequestRepository : IAttendanceApprovalRequestRepository
    {
        public List<AttendanceApprovalRequest> Requests { get; }
        public FakeAttendanceApprovalRequestRepository(IEnumerable<AttendanceApprovalRequest> seed) => Requests = seed.ToList();

        public Task<AttendanceApprovalRequest?> GetByIdAsync(string id) =>
            Task.FromResult(Requests.FirstOrDefault(r => r.Id == id));
        public Task<List<AttendanceApprovalRequest>> GetByIdsAsync(IEnumerable<string> ids) =>
            Task.FromResult(Requests.Where(r => ids.Contains(r.Id)).ToList());
        public Task<List<AttendanceApprovalRequest>> GetOpenByKindsAsync(IEnumerable<AttendanceApprovalKind> kinds) =>
            Task.FromResult(Requests.Where(r => r.ApprovalStatus == AttendanceApprovalStatus.PENDING && kinds.Contains(r.Kind)).ToList());
        public Task<List<AttendanceApprovalRequest>> GetByRecordIdsAsync(IEnumerable<string> recordIds) =>
            Task.FromResult(Requests.Where(r => recordIds.Contains(r.AttendanceRecordId)).ToList());
        public Task<List<AttendanceApprovalRequest>> GetByBreakIdsAsync(IEnumerable<string> breakIds) =>
            Task.FromResult(Requests.Where(r => r.AttendanceBreakId != null && breakIds.Contains(r.AttendanceBreakId)).ToList());
        public Task<List<AttendanceApprovalRequest>> GetForAuditAsync(string? employeeId, DateTime? from, DateTime? to, int limit = 500) =>
            Task.FromResult(Requests
                .Where(r => employeeId == null || r.EmployeeId == employeeId)
                .Where(r => from == null || r.EventAt >= from)
                .Where(r => to == null || r.EventAt <= to)
                .Take(limit).ToList());
        public Task<AttendanceApprovalRequest> AddAsync(AttendanceApprovalRequest request) { Requests.Add(request); return Task.FromResult(request); }
        public Task UpdateAsync(AttendanceApprovalRequest request) => Task.CompletedTask;   // mutated in place
        public Task UpdateRangeAsync(IEnumerable<AttendanceApprovalRequest> requests) => Task.CompletedTask;
    }

    // Never reached in the clock-out path — the record has no photo upload.
    private sealed class FakeAttendancePhotoStorage : IAttendancePhotoStorage
    {
        public Task<AttendancePhotoUploadResult> StoreAsync(AttendancePhotoUpload upload) => throw new NotImplementedException();
        public Task<AttendancePhotoFileResult?> GetAsync(string fileName) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(string fileName) => throw new NotImplementedException();
    }

    // Never reached — the record has no project, so the geofence check short-circuits.
    private sealed class FakeProjectService : IProjectService
    {
        public Task<IEnumerable<ProjectDto>> GetAllAsync() => Task.FromResult(Enumerable.Empty<ProjectDto>());
        public Task<ProjectDto?> GetByIdAsync(string id) => Task.FromResult<ProjectDto?>(null);
        public Task<ProjectDto> CreateAsync(SaveProjectDto dto) => throw new NotImplementedException();
        public Task<ProjectDto?> UpdateAsync(string id, SaveProjectDto dto) => throw new NotImplementedException();
        public Task<ProjectDto?> SetArchivedAsync(string id, bool archived) => throw new NotImplementedException();
    }

    // Never reached — geofence short-circuits before any policy lookup.
    private sealed class FakePolicyService : IPolicyService
    {
        public Task<IEnumerable<PolicyDto>> GetAllAsync() => Task.FromResult(Enumerable.Empty<PolicyDto>());
        public Task<PolicySaveResult> CreateAsync(SavePolicyDto dto) => throw new NotImplementedException();
        public Task<PolicySaveResult> UpdateAsync(string id, SavePolicyDto dto) => throw new NotImplementedException();
        public Task<PolicyDto?> SetArchivedAsync(string id, bool archived) => throw new NotImplementedException();
        public Task<PolicyDto?> SetDefaultAsync(string id) => throw new NotImplementedException();
        public Task<EmployeePolicy?> GetEffectivePolicyAsync(string employeeId) => Task.FromResult<EmployeePolicy?>(null);
        public Task<bool> RequiresGeofenceAsync(string employeeId) => Task.FromResult(false);
        public Task<IReadOnlyDictionary<string, double>> GetLeaveEntitlementsAsync(string employeeId) =>
            Task.FromResult<IReadOnlyDictionary<string, double>>(new Dictionary<string, double>());
        public Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>>>
            GetLeaveEntitlementsForEmployeesAsync(IEnumerable<string> employeeIds) =>
            Task.FromResult<IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>>>(
                new Dictionary<string, IReadOnlyDictionary<string, double>>());

        public Task<IReadOnlyList<EmployeePolicy>> GetAllAcrossOrgsAsync() =>
            Task.FromResult<IReadOnlyList<EmployeePolicy>>([]);

        public Task<IReadOnlyList<PolicyLeaveEntitlement>> GetAllPolicyEntitlementsAsync() =>
            Task.FromResult<IReadOnlyList<PolicyLeaveEntitlement>>([]);
    }

    // Not reached in the clock-out path — used by the per-policy auto-clock-out sweep.
    private sealed class FakeEmployeePolicyRepository : IEmployeePolicyRepository
    {
        public Task<List<EmployeePolicy>> GetAllAsync() => Task.FromResult(new List<EmployeePolicy>());
        public Task<List<EmployeePolicy>> GetAllAcrossOrgsAsync() => Task.FromResult(new List<EmployeePolicy>());
        public Task<EmployeePolicy?> GetByIdAsync(string id) => Task.FromResult<EmployeePolicy?>(null);
        public Task<EmployeePolicy?> GetByNameAsync(string name) => Task.FromResult<EmployeePolicy?>(null);
        public Task<EmployeePolicy?> GetDefaultAsync() => Task.FromResult<EmployeePolicy?>(null);
        public Task<EmployeePolicy> AddAsync(EmployeePolicy policy) => Task.FromResult(policy);
        public Task UpdateAsync(EmployeePolicy policy) => Task.CompletedTask;
        public Task ClearDefaultExceptAsync(string keepId) => Task.CompletedTask;
    }

    // Not reached in the clock-out path.
    private sealed class FakeOrganizationMembershipRepository : IOrganizationMembershipRepository
    {
        public Task<List<OrganizationMembership>> GetByUserAsync(string userId) => Task.FromResult(new List<OrganizationMembership>());
        public Task<OrganizationMembership?> GetAsync(string organizationId, string userId) => Task.FromResult<OrganizationMembership?>(null);
        public Task<List<OrganizationMembership>> GetForCurrentOrgAsync() => Task.FromResult(new List<OrganizationMembership>());
        public Task<OrganizationMembership?> GetForUserInCurrentOrgAsync(string userId) => Task.FromResult<OrganizationMembership?>(null);
        public Task<List<OrganizationMembership>> GetBySupervisorAsync(string supervisorId) => Task.FromResult(new List<OrganizationMembership>());
        public Task<int> CountByShiftIdAsync(string shiftId) => Task.FromResult(0);
        public Task AddAsync(OrganizationMembership membership) => Task.CompletedTask;
        public Task UpdateAsync(OrganizationMembership membership) => Task.CompletedTask;
    }
}
