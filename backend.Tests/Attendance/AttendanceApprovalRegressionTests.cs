using AltomateHR.Api.Modules.Teams.Dtos;
using AltomateHR.Api.Modules.Teams;
using AltomateHR.Api.Modules.Shifts.Entities;
using AltomateHR.Api.Modules.Shifts.Dtos;
using AltomateHR.Api.Modules.Shifts;
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
            shifts: new FakeShiftService(),
            currentUser: new FakeCurrentUser(),
            photos: new FakeAttendancePhotoStorage(),
            policies: new FakePolicyService(),
            supervision: new FakeSupervisionService(),
            // emp-1 needs an approver above them, or the CLOCK_OUT this test
            // files would be auto-approved on submission and never sit PENDING.
            router: new FakeApprovalRouter(new() { ["emp-1"] = [["sup-1"]] }),
            directory: TestDirectory.Over(new FakeOrganizationMembershipRepository()),
            realtime: new FakeRealtimeService(),
            notifications: new FakeNotificationService(),
            employees: new FakeEmployeeDirectory(),
            hours: new FakeHoursSummaryService(),
            teams: new FakeTeamService());

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

    // No shift assigned, so lateness falls back to the org's working hours —
    // which is what an org that hasn't configured shifts actually looks like.
    // --- an unfinished shift blocks the next one ---
    //
    // Auto-clock-out only runs when the employee's policy opts in, so without
    // this guard a forgotten clock-out is never resolved: a second session
    // starts, the first stays open forever, and its hours are never counted.

    [Fact]
    public async Task ClockIn_IsRefused_WhileAnEarlierShiftIsStillOpen()
    {
        var now = DateTime.UtcNow;
        var yesterday = AttendanceTime.StartOfLocalDay(now.AddDays(-1));
        var service = BuildService([OpenRecord("rec-open", "emp-1", yesterday, now.AddDays(-1))]);

        var result = await service.ClockInAsync("emp-1", new ClockInDto());

        Assert.False(result.Ok);
        Assert.Equal("OPEN_SESSION_REQUIRES_CLOCK_OUT", result.Code);
        // The open record comes back so the client can name the day.
        Assert.Equal("rec-open", result.Record?.Id);
    }

    [Fact]
    public async Task ClockOut_ClosesAnEarlierOpenShift_RatherThanRefusing()
    {
        // The other half of the rule. Scoped to today, clock-out would answer
        // "you haven't clocked in today" and the employee would be stuck: unable
        // to clock in because a session is open, unable to close it because it
        // isn't today's.
        var now = DateTime.UtcNow;
        var yesterday = AttendanceTime.StartOfLocalDay(now.AddDays(-1));
        var open = OpenRecord("rec-open", "emp-1", yesterday, now.AddDays(-1));
        var service = BuildService([open]);

        var result = await service.ClockOutAsync("emp-1", new ClockOutDto());

        Assert.True(result.Ok);
        Assert.NotNull(open.TimeOut);
    }

    [Fact]
    public async Task ClockIn_IsAllowed_OnceNothingIsOpen()
    {
        var now = DateTime.UtcNow;
        var yesterday = AttendanceTime.StartOfLocalDay(now.AddDays(-1));
        var closed = OpenRecord("rec-closed", "emp-1", yesterday, now.AddDays(-1));
        closed.TimeOut = now.AddDays(-1).AddHours(8);
        var service = BuildService([closed]);

        var result = await service.ClockInAsync("emp-1", new ClockInDto());

        Assert.True(result.Ok);
    }

    [Fact]
    public async Task ClockIn_IsNotBlockedByTodaysOwnRecord()
    {
        // Today's row is handled by the existing "already clocked in" path, which
        // returns a different message. The open-session guard must not swallow it.
        var now = DateTime.UtcNow;
        var today = AttendanceTime.StartOfLocalDay(now);
        var service = BuildService([OpenRecord("rec-today", "emp-1", today, now.AddHours(-2))]);

        var result = await service.ClockInAsync("emp-1", new ClockInDto());

        Assert.False(result.Ok);
        Assert.Null(result.Code);
        Assert.Contains("already clocked in", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    // --- nobody above you ---
    //
    // BuildService's router has no chain, which is exactly the top-of-hierarchy
    // case: admins don't approve (see OrgRoles), so the person at the top has
    // zero steps. A PENDING request there could never be seen or decided.

    [Fact]
    public async Task ClockIn_IsDecidedOnSubmission_WhenNobodyIsAboveTheEmployee()
    {
        var service = BuildService([]);

        var result = await service.ClockInAsync("emp-1", new ClockInDto());

        Assert.True(result.Ok);
        var clockIn = Assert.Single(result.Record!.Approvals!, a => a.Kind == AttendanceApprovalKind.CLOCK_IN);
        Assert.Equal(AttendanceApprovalStatus.APPROVED, clockIn.ApprovalStatus);
    }

    [Fact]
    public async Task AnAutoApprovedRequest_RecordsNoReviewer()
    {
        // Nobody reviewed it. Stamping the applicant as their own approver would
        // make the audit trail claim a review that never happened.
        var approvals = new FakeAttendanceApprovalRequestRepository([]);
        var service = BuildService([], approvals);

        await service.ClockInAsync("emp-1", new ClockInDto());

        Assert.Null(Assert.Single(approvals.Requests).ReviewerId);
    }

    [Fact]
    public async Task ClockIn_StillWaitsForAnApprover_WhenOneExists()
    {
        // Keeps the rule narrow: the ordinary employee path is untouched.
        var service = BuildService([], router: new FakeApprovalRouter(new() { ["emp-1"] = [["sup-1"]] }));

        var result = await service.ClockInAsync("emp-1", new ClockInDto());

        Assert.True(result.Ok);
        var clockIn = Assert.Single(result.Record!.Approvals!, a => a.Kind == AttendanceApprovalKind.CLOCK_IN);
        Assert.Equal(AttendanceApprovalStatus.PENDING, clockIn.ApprovalStatus);
    }

    // --- reconciling what the rule change stranded ---

    [Fact]
    public async Task Reconcile_ResolvesRequestsParkedOnAStepThatNoLongerExists()
    {
        // The concrete case: requests sat at step 1 because an admin occupied
        // that layer. Excluding admins shortened the chain to one step, so
        // step 1 resolves to nobody and the request became unreachable.
        var approvals = new FakeAttendanceApprovalRequestRepository([
            PendingAt(step: 1),
        ]);
        var service = BuildService([], approvals,
            router: new FakeApprovalRouter(new() { ["emp-1"] = [["sup-1"]] }));   // one step only

        var resolved = await service.ReconcileUnreachableApprovalsAsync(apply: true);

        Assert.Equal(1, resolved);
        Assert.Equal(AttendanceApprovalStatus.APPROVED, Assert.Single(approvals.Requests).ApprovalStatus);
    }

    [Fact]
    public async Task Reconcile_LeavesRequestsThatStillHaveAnApprover()
    {
        var approvals = new FakeAttendanceApprovalRequestRepository([
            PendingAt(step: 0),
        ]);
        var service = BuildService([], approvals,
            router: new FakeApprovalRouter(new() { ["emp-1"] = [["sup-1"]] }));

        var resolved = await service.ReconcileUnreachableApprovalsAsync(apply: true);

        Assert.Equal(0, resolved);
        Assert.Equal(AttendanceApprovalStatus.PENDING, Assert.Single(approvals.Requests).ApprovalStatus);
    }

    [Fact]
    public async Task Reconcile_WithoutApply_CountsButChangesNothing()
    {
        var approvals = new FakeAttendanceApprovalRequestRepository([PendingAt(step: 1)]);
        var service = BuildService([], approvals,
            router: new FakeApprovalRouter(new() { ["emp-1"] = [["sup-1"]] }));

        var found = await service.ReconcileUnreachableApprovalsAsync(apply: false);

        Assert.Equal(1, found);
        Assert.Equal(AttendanceApprovalStatus.PENDING, Assert.Single(approvals.Requests).ApprovalStatus);
    }

    [Fact]
    public async Task Reconcile_IsIdempotent()
    {
        // A resolved row is no longer PENDING, so a second run finds nothing.
        var approvals = new FakeAttendanceApprovalRequestRepository([PendingAt(step: 1)]);
        var service = BuildService([], approvals,
            router: new FakeApprovalRouter(new() { ["emp-1"] = [["sup-1"]] }));

        Assert.Equal(1, await service.ReconcileUnreachableApprovalsAsync(apply: true));
        Assert.Equal(0, await service.ReconcileUnreachableApprovalsAsync(apply: true));
    }

    private static AttendanceApprovalRequest PendingAt(int step) => new()
    {
        Id = $"req-{step}",
        EmployeeId = "emp-1",
        Kind = AttendanceApprovalKind.CLOCK_IN,
        AttendanceRecordId = "rec-1",
        CurrentStep = step,
        ApprovalStatus = AttendanceApprovalStatus.PENDING,
        SubmittedAt = DateTime.UtcNow.AddDays(-30),
    };

    // --- you can only clock into a project you're on ---
    //
    // The picker listed every project in the org and the server stored whatever
    // it was sent, so anyone could tag their day to a site they have no part in.
    // Membership decides whose team view you appear in, so the two must agree.

    [Fact]
    public async Task ClockIn_IsRefused_ForAProjectTheEmployeeIsNotOn()
    {
        var teams = new FakeTeamService { ProjectsOf = { ["emp-1"] = ["proj-mine"] } };
        var service = BuildService([], teams: teams);

        var result = await service.ClockInAsync("emp-1", new ClockInDto { ProjectId = "proj-theirs" });

        Assert.False(result.Ok);
        Assert.Equal("NOT_ON_PROJECT", result.Code);
    }

    [Fact]
    public async Task ClockIn_IsAllowed_ForAProjectTheEmployeeIsOn()
    {
        var teams = new FakeTeamService { ProjectsOf = { ["emp-1"] = ["proj-mine"] } };
        var service = BuildService([], teams: teams);

        var result = await service.ClockInAsync("emp-1", new ClockInDto { ProjectId = "proj-mine" });

        Assert.True(result.Ok);
        Assert.Equal("proj-mine", result.Record!.ProjectId);
    }

    [Fact]
    public async Task ClockIn_WithNoProject_IsStillAllowed()
    {
        // Someone on no team has no project to pick. Attendance without a
        // project is valid, so the guard must not lock them out of clocking in
        // entirely.
        var service = BuildService([], teams: new FakeTeamService());

        var result = await service.ClockInAsync("emp-1", new ClockInDto());

        Assert.True(result.Ok);
        Assert.Null(result.Record!.ProjectId);
    }

    // --- several shifts in one day ---
    //
    // A day is a record; each stint is a session under it. The record's totals
    // are a roll-up, and the gap between stints is not worked time.

    [Fact]
    public async Task ClockIn_IsAllowedAgain_AfterClockingOutEarlierTheSameDay()
    {
        var service = BuildService([]);

        await service.ClockInAsync("emp-1", new ClockInDto());
        await service.ClockOutAsync("emp-1", new ClockOutDto());
        var second = await service.ClockInAsync("emp-1", new ClockInDto());

        Assert.True(second.Ok);
        Assert.Null(second.Record!.TimeOut);   // back on the clock
    }

    [Fact]
    public async Task ASecondShift_IsStillRefusedWhileTheFirstIsOpen()
    {
        // The guard that matters: only an OPEN stint blocks a new one.
        var service = BuildService([]);

        await service.ClockInAsync("emp-1", new ClockInDto());
        var again = await service.ClockInAsync("emp-1", new ClockInDto());

        Assert.False(again.Ok);
        Assert.Contains("already clocked in", again.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheDay_SumsItsShiftsRatherThanSpanningThem()
    {
        // The whole reason sessions own the clock. Two 1-hour stints three hours
        // apart is 2h worked, not the 5h that last-minus-first would report —
        // and that number feeds counted hours and pay.
        var sessions = new FakeAttendanceSessionRepository([]);
        var repo = new FakeAttendanceRepository([]);
        var service = BuildService([], repo: repo, sessions: sessions);

        await service.ClockInAsync("emp-1", new ClockInDto());
        await service.ClockOutAsync("emp-1", new ClockOutDto());
        // Backdate the closed stint so the two are genuinely apart.
        var first = sessions.All.Single();
        first.StartedAt = DateTime.UtcNow.AddHours(-5);
        first.EndedAt = DateTime.UtcNow.AddHours(-4);
        first.DurationMin = 60;

        await service.ClockInAsync("emp-1", new ClockInDto());
        var second = sessions.All.Last();
        second.StartedAt = DateTime.UtcNow.AddHours(-1);
        var result = await service.ClockOutAsync("emp-1", new ClockOutDto());

        Assert.True(result.Ok);
        // ~120 minutes of work across a 5-hour span.
        Assert.InRange(result.Record!.DurationMin!.Value, 118, 122);
    }

    [Fact]
    public async Task ASecondShift_DoesNotOverwriteTheFirstShiftsProof()
    {
        // The reason the evidence moved onto the session: an off-site morning
        // must survive an afternoon clock-in.
        var sessions = new FakeAttendanceSessionRepository([]);
        var service = BuildService([], sessions: sessions);

        await service.ClockInAsync("emp-1", new ClockInDto { PhotoUrl = "/attendance/photos/morning.jpg" });
        await service.ClockOutAsync("emp-1", new ClockOutDto());
        await service.ClockInAsync("emp-1", new ClockInDto { PhotoUrl = "/attendance/photos/afternoon.jpg" });

        Assert.Equal("/attendance/photos/morning.jpg", sessions.All.First().ClockInPhotoUrl);
        Assert.Equal("/attendance/photos/afternoon.jpg", sessions.All.Last().ClockInPhotoUrl);
    }

    [Fact]
    public async Task StartingASecondShift_DoesNotHideTheFirstOne()
    {
        // The record only reports first-start / last-end, so once a second shift
        // opens, timeOut goes back to null and the day looks like one long
        // unfinished stint — the morning appears to have vanished. The stints
        // ride along so the client can still show it.
        var service = BuildService([]);

        await service.ClockInAsync("emp-1", new ClockInDto());
        await service.ClockOutAsync("emp-1", new ClockOutDto());
        var second = await service.ClockInAsync("emp-1", new ClockInDto());

        var sessions = second.Record!.Sessions;
        Assert.Equal(2, sessions.Count);
        Assert.NotNull(sessions[0].EndedAt);   // the morning, still there
        Assert.Null(sessions[1].EndedAt);      // the one running now
    }

    private static IEnumerable<AttendanceSession> SessionsFor(IEnumerable<AttendanceRecord> records) =>
        records
            .Where(r => r.TimeIn is not null)
            .Select(r => new AttendanceSession
            {
                Id = $"sess-{r.Id}",
                AttendanceRecordId = r.Id,
                EmployeeId = r.EmployeeId,
                StartedAt = r.TimeIn!.Value,
                EndedAt = r.TimeOut,
                DurationMin = r.TimeOut is null
                    ? null
                    : (int)Math.Round((r.TimeOut.Value - r.TimeIn.Value).TotalMinutes),
                Status = r.Status,
            });

    private static AttendanceRecord OpenRecord(string id, string employeeId, DateTime date, DateTime timeIn) =>
        new()
        {
            Id = id,
            EmployeeId = employeeId,
            Date = date,
            TimeIn = timeIn,
            TimeOut = null,
            ProjectId = null,   // no project → no geofence, so no GPS proof needed
            Status = AttendanceStatus.CLOCKED_IN,
            CreatedAt = timeIn,
            UpdatedAt = timeIn,
        };

    private static AttendanceService BuildService(
        IEnumerable<AttendanceRecord> records,
        FakeAttendanceApprovalRequestRepository? approvals = null,
        FakeApprovalRouter? router = null,
        FakeTeamService? teams = null,
        FakeAttendanceRepository? repo = null,
        FakeAttendanceSessionRepository? sessions = null) =>
        new(
            repo: repo ?? new FakeAttendanceRepository(records),
            // Sessions derived from the records, exactly as the multi-shift
            // migration backfills real data: one session per pre-existing
            // record, open or closed to match. An open shift is now defined by
            // an open SESSION, so a record without one isn't clocked in.
            sessions: sessions ?? new FakeAttendanceSessionRepository(SessionsFor(records)),
            breaks: new FakeAttendanceBreakRepository(),
            approvalRequests: approvals ?? new FakeAttendanceApprovalRequestRepository([]),
            projects: new FakeProjectService(),
            organizations: new FakeOrganizationService(),
            shifts: new FakeShiftService(),
            currentUser: new FakeCurrentUser(),
            photos: new FakeAttendancePhotoStorage(),
            policies: new FakePolicyService(),
            supervision: new FakeSupervisionService(),
            router: router ?? new FakeApprovalRouter(),
            directory: TestDirectory.Over(new FakeOrganizationMembershipRepository()),
            realtime: new FakeRealtimeService(),
            notifications: new FakeNotificationService(),
            employees: new FakeEmployeeDirectory(),
            hours: new FakeHoursSummaryService(),
            teams: teams ?? new FakeTeamService());

    private sealed class FakeShiftService : IShiftService
    {
        public Task<Shift?> GetEffectiveShiftAsync(string employeeId) => Task.FromResult<Shift?>(null);
        public Task<Shift?> GetByIdAsync(string id) => Task.FromResult<Shift?>(null);
        public Task<IEnumerable<ShiftDto>> GetAllAsync() =>
            Task.FromResult<IEnumerable<ShiftDto>>([]);
        public Task<IEnumerable<ShiftDto>> GetForProjectAsync(string projectId) =>
            Task.FromResult<IEnumerable<ShiftDto>>([]);
        public Task<ShiftSaveResult> CreateAsync(CreateShiftDto dto) => throw new NotSupportedException();
        public Task<ShiftSaveResult> UpdateAsync(string id, UpdateShiftDto dto) => throw new NotSupportedException();
        public Task<ShiftDeleteResult> DeleteAsync(string id) => throw new NotSupportedException();
        public Task<ShiftSaveResult> SetDefaultAsync(string id) => throw new NotSupportedException();
    }

    private sealed class FakeAttendanceRepository : IAttendanceRepository
    {
        private readonly List<AttendanceRecord> _records;
        public FakeAttendanceRepository(IEnumerable<AttendanceRecord> records) => _records = records.ToList();

        // Matches on the date properly: since clock-in started refusing while an
        // earlier session is open, "today's row" and "the open row" can be
        // different records and a date-blind fake would hide that.
        public Task<AttendanceRecord?> GetForEmployeeOnDateAsync(string employeeId, DateTime date) =>
            Task.FromResult(_records.FirstOrDefault(r => r.EmployeeId == employeeId && r.Date == date));

        public Task<List<AttendanceRecord>> GetForEmployeesOnDateAsync(
            IEnumerable<string> employeeIds, DateTime date) =>
            Task.FromResult(_records.Where(r => employeeIds.Contains(r.EmployeeId) && r.Date == date).ToList());

        public Task<List<AttendanceRecord>> GetByIdsAsync(IEnumerable<string> ids) =>
            Task.FromResult(_records.Where(r => ids.Contains(r.Id)).ToList());

        public Task<AttendanceRecord?> GetOpenForEmployeeAsync(string employeeId) =>
            Task.FromResult(
                _records
                    .Where(r => r.EmployeeId == employeeId && r.TimeIn != null && r.TimeOut == null)
                    .OrderByDescending(r => r.Date)
                    .FirstOrDefault());

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

        // Ordered as the real repository returns them, for tests that assert on
        // which stint holds what.
        public IReadOnlyList<AttendanceSession> All => _sessions.OrderBy(s => s.StartedAt).ToList();

        public Task<AttendanceSession?> GetOpenForRecordAsync(string attendanceRecordId) =>
            Task.FromResult(_sessions.FirstOrDefault(s => s.AttendanceRecordId == attendanceRecordId && s.EndedAt == null));
        public Task<AttendanceSession?> GetByIdAsync(string id) =>
            Task.FromResult(_sessions.FirstOrDefault(s => s.Id == id));
        public Task<List<AttendanceSession>> GetByRecordIdsAsync(IEnumerable<string> attendanceRecordIds) =>
            Task.FromResult(_sessions
                .Where(s => attendanceRecordIds.Contains(s.AttendanceRecordId))
                .OrderBy(s => s.StartedAt)
                .ToList());

        public Task<List<AttendanceSession>> GetByRecordAsync(string attendanceRecordId) =>
            Task.FromResult(_sessions
                .Where(s => s.AttendanceRecordId == attendanceRecordId)
                .OrderBy(s => s.StartedAt)
                .ToList());
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
        public Task<IReadOnlyDictionary<string, EmployeePolicy>>
            GetEffectivePoliciesForEmployeesAsync(IEnumerable<string> employeeIds) =>
            Task.FromResult<IReadOnlyDictionary<string, EmployeePolicy>>(
                new Dictionary<string, EmployeePolicy>());
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
    // Team presence isn't what these tests exercise; an empty org keeps the
    // dependency honest without inventing a hierarchy.
    private sealed class FakeTeamService : ITeamService
    {
        public Task<IEnumerable<TeamDto>> GetAllAsync() => Task.FromResult<IEnumerable<TeamDto>>([]);
        public Task<TeamSaveResult> CreateAsync(CreateTeamDto dto) => throw new NotSupportedException();
        public Task<TeamSaveResult> UpdateAsync(string id, SaveTeamDto dto) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(string id) => Task.FromResult(false);
        public Task<TeamSaveResult> AddOrUpdateMemberAsync(string teamId, SaveMembershipDto dto) =>
            throw new NotSupportedException();
        public Task<TeamSaveResult> RemoveMemberAsync(string teamId, string employeeId) =>
            throw new NotSupportedException();
        public Task<IEnumerable<ApprovalStepDto>> GetApprovalChainAsync(
            string employeeId, ApprovalModule module, string? projectId = null) =>
            Task.FromResult<IEnumerable<ApprovalStepDto>>([]);
        public Task<IReadOnlyList<string>> GetMemberEmployeeIdsAsync(string teamId) =>
            Task.FromResult<IReadOnlyList<string>>([]);
        public Task<IReadOnlyList<SupervisedTeamDto>> GetSupervisedTeamsAsync(string userId) =>
            Task.FromResult<IReadOnlyList<SupervisedTeamDto>>([]);
        public Task<IReadOnlyList<string>> GetReportEmployeeIdsAsync(string supervisorId) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        // These tests clock in without a project, so the membership check never
        // fires; ProjectsOf lets a test opt into one when it needs to.
        public Dictionary<string, List<string>> ProjectsOf { get; init; } = [];

        public Task<IReadOnlyList<string>> GetProjectIdsForMemberAsync(string employeeId) =>
            Task.FromResult<IReadOnlyList<string>>(ProjectsOf.GetValueOrDefault(employeeId, []));

        public Task<IReadOnlyList<LayerApproverOptionsDto>?> GetApproverOptionsAsync(string teamId, string employeeId) =>
            Task.FromResult<IReadOnlyList<LayerApproverOptionsDto>?>(null);
        public Task<ApproverOverrideResult> SetApproverOverrideAsync(
            string teamId, string employeeId, int layer, List<string> approverIds) =>
            throw new NotSupportedException();
        public Task<ApproverOverrideResult> ClearApproverOverrideAsync(string teamId, string employeeId, int layer) =>
            throw new NotSupportedException();
    }

}
