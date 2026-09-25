using AltomateHR.Api.Modules.Projects.Entities;
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
    // Every clock carries a GPS fix. Not because anything here checks a
    // geofence — none of these projects has one — but because clocking in OR
    // out without coordinates is refused outright (LOCATION_REQUIRED): a
    // project with no geofenced site has nothing else recording where the
    // shift started or ended. The value is arbitrary; only its presence matters.
    private const double ClockLat = 3.1390;
    private const double ClockLng = 101.6869;

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
            teams: new FakeTeamService(),
            holidays: new FakeHolidayService(),
            xero: new StubXeroForAttendance(),
            leave: new AltomateHR.Api.Tests.Payroll.StubPayrollLeave(),
            leaveTypes: new FakeLeaveTypeService());

        // --- Act: employee clocks out. ---
        var result = await service.ClockOutAsync("emp-1", new ClockOutDto { Lat = ClockLat, Lng = ClockLng });

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

        var result = await service.ClockInAsync("emp-1", new ClockInDto { Lat = ClockLat, Lng = ClockLng });

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

        var result = await service.ClockOutAsync("emp-1", new ClockOutDto { Lat = ClockLat, Lng = ClockLng });

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

        var result = await service.ClockInAsync("emp-1", new ClockInDto { Lat = ClockLat, Lng = ClockLng });

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

        var result = await service.ClockInAsync("emp-1", new ClockInDto { Lat = ClockLat, Lng = ClockLng });

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

        var result = await service.ClockInAsync("emp-1", new ClockInDto { Lat = ClockLat, Lng = ClockLng });

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

        await service.ClockInAsync("emp-1", new ClockInDto { Lat = ClockLat, Lng = ClockLng });

        Assert.Null(Assert.Single(approvals.Requests).ReviewerId);
    }

    [Fact]
    public async Task ClockIn_StillWaitsForAnApprover_WhenOneExists()
    {
        // Keeps the rule narrow: the ordinary employee path is untouched.
        var service = BuildService([], router: new FakeApprovalRouter(new() { ["emp-1"] = [["sup-1"]] }));

        var result = await service.ClockInAsync("emp-1", new ClockInDto { Lat = ClockLat, Lng = ClockLng });

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

        var result = await service.ClockInAsync("emp-1", new ClockInDto { Lat = ClockLat, Lng = ClockLng, ProjectId = "proj-theirs" });

        Assert.False(result.Ok);
        Assert.Equal("NOT_ON_PROJECT", result.Code);
    }

    [Fact]
    public async Task ClockIn_IsAllowed_ForAProjectTheEmployeeIsOn()
    {
        var teams = new FakeTeamService { ProjectsOf = { ["emp-1"] = ["proj-mine"] } };
        var service = BuildService([], teams: teams);

        var result = await service.ClockInAsync("emp-1", new ClockInDto { Lat = ClockLat, Lng = ClockLng, ProjectId = "proj-mine" });

        Assert.True(result.Ok);
        Assert.Equal("proj-mine", result.Record!.ProjectId);
    }

    // --- a clock-in has to say where it happened ---
    //
    // The geofence only speaks for projects that HAVE a geofenced site. On one
    // that doesn't — and on no project at all — nothing above ever asked where
    // the employee was, so a denied browser permission produced a shift with no
    // location on file and nothing for an admin to audit afterwards.

    [Fact]
    public async Task ClockIn_IsRefused_WhenNoLocationWasCaptured()
    {
        var service = BuildService([], teams: new FakeTeamService());

        var result = await service.ClockInAsync("emp-1", new ClockInDto());

        Assert.False(result.Ok);
        Assert.Equal("LOCATION_REQUIRED", result.Code);
    }

    [Fact]
    public async Task ClockIn_WithoutLocation_IsAllowed_WithARemarkAndPhoto()
    {
        // The escape hatch, and the reason this isn't a hard block: a device
        // that genuinely can't get a fix would otherwise be unable to start a
        // shift at all. The proof is what the approver judges instead.
        var service = BuildService([], teams: new FakeTeamService());

        var result = await service.ClockInAsync("emp-1", new ClockInDto
        {
            Remark = "GPS won't lock on inside the basement car park.",
            PhotoUrl = "/attendance/photos/entrance.jpg",
        });

        Assert.True(result.Ok);
    }

    // The other end of the shift gets the same rule. Before it, a denied
    // location prompt produced a clock-out with no location and no photo —
    // the one event in the day nobody could verify.
    [Fact]
    public async Task ClockOut_IsRefused_WhenNoLocationWasCaptured()
    {
        var service = BuildService([], teams: new FakeTeamService());
        await service.ClockInAsync("emp-1", new ClockInDto { Lat = ClockLat, Lng = ClockLng });

        var result = await service.ClockOutAsync("emp-1", new ClockOutDto());

        Assert.False(result.Ok);
        Assert.Equal("LOCATION_REQUIRED", result.Code);
        Assert.Contains("clock out", result.Error);
    }

    [Fact]
    public async Task ClockOut_WithoutLocation_IsAllowed_WithARemarkAndPhoto()
    {
        var service = BuildService([], teams: new FakeTeamService());
        await service.ClockInAsync("emp-1", new ClockInDto { Lat = ClockLat, Lng = ClockLng });

        var result = await service.ClockOutAsync("emp-1", new ClockOutDto
        {
            Remark = "Phone's location is switched off.",
            PhotoUrl = "/attendance/photos/leaving.jpg",
        });

        Assert.True(result.Ok);
    }

    [Fact]
    public async Task ClockIn_StoresTheCoordinates_OnAProjectWithNoGeofence()
    {
        // Captured, not just demanded: the point of the refusal above is that
        // the fix ends up on the record, where an admin can read it back.
        var teams = new FakeTeamService { ProjectsOf = { ["emp-1"] = ["proj-mine"] } };
        var service = BuildService([], teams: teams);

        var result = await service.ClockInAsync("emp-1", new ClockInDto
        {
            ProjectId = "proj-mine",
            Lat = ClockLat,
            Lng = ClockLng,
        });

        Assert.True(result.Ok);
        Assert.Equal(ClockLat, result.Record!.ClockInLat);
        Assert.Equal(ClockLng, result.Record!.ClockInLng);
    }

    [Fact]
    public async Task ClockIn_WithNoProject_IsStillAllowed()
    {
        // Someone on no team has no project to pick. Attendance without a
        // project is valid, so the guard must not lock them out of clocking in
        // entirely.
        var service = BuildService([], teams: new FakeTeamService());

        var result = await service.ClockInAsync("emp-1", new ClockInDto { Lat = ClockLat, Lng = ClockLng });

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

        await service.ClockInAsync("emp-1", new ClockInDto { Lat = ClockLat, Lng = ClockLng });
        await service.ClockOutAsync("emp-1", new ClockOutDto { Lat = ClockLat, Lng = ClockLng });
        var second = await service.ClockInAsync("emp-1", new ClockInDto { Lat = ClockLat, Lng = ClockLng });

        Assert.True(second.Ok);
        Assert.Null(second.Record!.TimeOut);   // back on the clock
    }

    [Fact]
    public async Task ASecondShift_IsStillRefusedWhileTheFirstIsOpen()
    {
        // The guard that matters: only an OPEN stint blocks a new one.
        var service = BuildService([]);

        await service.ClockInAsync("emp-1", new ClockInDto { Lat = ClockLat, Lng = ClockLng });
        var again = await service.ClockInAsync("emp-1", new ClockInDto { Lat = ClockLat, Lng = ClockLng });

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

        await service.ClockInAsync("emp-1", new ClockInDto { Lat = ClockLat, Lng = ClockLng });
        await service.ClockOutAsync("emp-1", new ClockOutDto { Lat = ClockLat, Lng = ClockLng });
        // Backdate the closed stint so the two are genuinely apart.
        var first = sessions.All.Single();
        first.StartedAt = DateTime.UtcNow.AddHours(-5);
        first.EndedAt = DateTime.UtcNow.AddHours(-4);
        first.DurationMin = 60;

        await service.ClockInAsync("emp-1", new ClockInDto { Lat = ClockLat, Lng = ClockLng });
        var second = sessions.All.Last();
        second.StartedAt = DateTime.UtcNow.AddHours(-1);
        var result = await service.ClockOutAsync("emp-1", new ClockOutDto { Lat = ClockLat, Lng = ClockLng });

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

        await service.ClockInAsync("emp-1", new ClockInDto { Lat = ClockLat, Lng = ClockLng, PhotoUrl = "/attendance/photos/morning.jpg" });
        await service.ClockOutAsync("emp-1", new ClockOutDto { Lat = ClockLat, Lng = ClockLng });
        await service.ClockInAsync("emp-1", new ClockInDto { Lat = ClockLat, Lng = ClockLng, PhotoUrl = "/attendance/photos/afternoon.jpg" });

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

        await service.ClockInAsync("emp-1", new ClockInDto { Lat = ClockLat, Lng = ClockLng });
        await service.ClockOutAsync("emp-1", new ClockOutDto { Lat = ClockLat, Lng = ClockLng });
        var second = await service.ClockInAsync("emp-1", new ClockInDto { Lat = ClockLat, Lng = ClockLng });

        var sessions = second.Record!.Sessions;
        Assert.Equal(2, sessions.Count);
        Assert.NotNull(sessions[0].EndedAt);   // the morning, still there
        Assert.Null(sessions[1].EndedAt);      // the one running now
    }

    // --- an approved correction lands on the shift, not just the day ---
    //
    // The day on screen: shift 1 14:41–15:43, shift 2 opened at 15:43 and not
    // clocked out until 10:09 the next morning, then corrected to 18:00. The
    // correction used to rewrite only the record, so the shift still read
    // 18h 26m beside a day total of 3h 19m — and the next roll-up put the
    // uncorrected hours back.

    private static readonly DateTime DayStart = DateTime.UtcNow.AddHours(-22);   // "14:41"

    // Clocks two shifts through the real service, then moves them onto the
    // screenshot's timeline. The record is re-aligned by hand because nothing
    // re-runs the roll-up after a test moves a session.
    private static async Task<(AttendanceService Service, FakeAttendanceSessionRepository Sessions, AttendanceRecord Record)>
        OvernightSplitShiftDay()
    {
        var sessions = new FakeAttendanceSessionRepository([]);
        var repo = new FakeAttendanceRepository([]);
        var service = BuildService([], repo: repo, sessions: sessions);

        await service.ClockInAsync("emp-1", new ClockInDto { Lat = ClockLat, Lng = ClockLng });
        await service.ClockOutAsync("emp-1", new ClockOutDto { Lat = ClockLat, Lng = ClockLng });
        await service.ClockInAsync("emp-1", new ClockInDto { Lat = ClockLat, Lng = ClockLng });
        var closed = await service.ClockOutAsync("emp-1", new ClockOutDto { Lat = ClockLat, Lng = ClockLng });

        var (first, second) = (sessions.All[0], sessions.All[1]);
        first.StartedAt = DayStart;
        first.EndedAt = DayStart.AddMinutes(62);            // 15:43
        first.DurationMin = 62;
        second.StartedAt = DayStart.AddMinutes(62);         // 15:43
        second.EndedAt = DayStart.AddMinutes(62 + 1106);    // 10:09 +1d
        second.DurationMin = 1106;

        var record = (await repo.GetByIdAsync(closed.Record!.Id))!;
        record.TimeIn = first.StartedAt;
        record.TimeOut = second.EndedAt;
        record.DurationMin = 62 + 1106;
        return (service, sessions, record);
    }

    [Fact]
    public async Task AnApprovedClockOutCorrection_EndsTheShiftItCorrects()
    {
        var (service, sessions, record) = await OvernightSplitShiftDay();
        var corrected = DayStart.AddMinutes(199);           // 18:00

        // No approver above emp-1, so the correction is approved on filing.
        var result = await service.SubmitTimeAdjustmentAsync("emp-1", new SubmitTimeAdjustmentDto
        {
            RecordId = record.Id,
            RequestedTimeOut = corrected,
            Reason = "Forgot to clock out.",
        });

        Assert.True(result.Ok);
        var second = sessions.All[1];
        Assert.Equal(corrected, second.EndedAt);
        Assert.Equal(137, second.DurationMin);              // 15:43–18:00, not 18h 26m
        Assert.Equal(62, sessions.All[0].DurationMin);      // the other shift untouched
        // The day is the sum of its shifts, and agrees with them.
        Assert.Equal(corrected, record.TimeOut);
        Assert.Equal(199, record.DurationMin);
    }

    [Fact]
    public async Task AnApprovedCorrection_SurvivesTheNextClockInThatDay()
    {
        // The roll-up re-derives the day from its shifts on every clock event.
        // With the correction only on the record, clocking in again put the
        // uncorrected 18h 26m straight back into the day's hours.
        var (service, sessions, record) = await OvernightSplitShiftDay();
        await service.SubmitTimeAdjustmentAsync("emp-1", new SubmitTimeAdjustmentDto
        {
            RecordId = record.Id,
            RequestedTimeOut = DayStart.AddMinutes(199),
            Reason = "Forgot to clock out.",
        });

        await service.ClockInAsync("emp-1", new ClockInDto { Lat = ClockLat, Lng = ClockLng });
        await service.ClockOutAsync("emp-1", new ClockOutDto { Lat = ClockLat, Lng = ClockLng });

        Assert.Equal(137, sessions.All[1].DurationMin);
        // 62 + 137, plus the few seconds the third shift lasted.
        Assert.InRange(record.DurationMin!.Value, 199, 200);
    }

    [Fact]
    public async Task AClockOutCorrection_BeforeTheLastShiftStarted_IsRefused()
    {
        // 15:00 is after the day's 14:41 start, so the day-level check passed —
        // but it's before shift 2's 15:43 start, and would have left that shift
        // ending before it began.
        var (service, sessions, record) = await OvernightSplitShiftDay();

        var result = await service.SubmitTimeAdjustmentAsync("emp-1", new SubmitTimeAdjustmentDto
        {
            RecordId = record.Id,
            RequestedTimeOut = DayStart.AddMinutes(19),     // 15:00
            Reason = "Left early.",
        });

        Assert.False(result.Ok);
        Assert.Equal(DayStart.AddMinutes(62 + 1106), sessions.All[1].EndedAt);
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
            teams: teams ?? new FakeTeamService(),
            holidays: new FakeHolidayService(),
            xero: new StubXeroForAttendance(),
            leave: new AltomateHR.Api.Tests.Payroll.StubPayrollLeave(),
            leaveTypes: new FakeLeaveTypeService());

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
        public Task<ShiftSaveResult> SetArchivedAsync(string id, bool archived) => throw new NotSupportedException();
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

    // ---- Adjustment dates ----
    //
    // A correction carries a DATE as well as a time, because a shift left open
    // overnight is closed the next day and the employee has to be able to say
    // which day they actually stopped. That also makes it possible to pick the
    // wrong day, and a clock-out before its own clock-in would compute negative
    // hours all the way through to payroll.

    [Fact]
    public async Task SubmitTimeAdjustment_AcceptsACorrectionOnTheDayBefore()
    {
        // Clocked in 08:00 yesterday, forgot to clock out, closing it this
        // morning. The real end was 18:00 YESTERDAY.
        var now = new DateTime(2026, 9, 16, 9, 0, 0, DateTimeKind.Utc);
        var (service, approvals) = AdjustmentService(
            timeIn: now.AddDays(-1).Date.AddHours(8), timeOut: now);

        var result = await service.SubmitTimeAdjustmentAsync("emp-1", new SubmitTimeAdjustmentDto
        {
            RecordId = "rec-1",
            RequestedTimeOut = now.AddDays(-1).Date.AddHours(18),
            Reason = "Forgot to clock out before leaving site.",
        });

        Assert.True(result.Ok);
        var filed = Assert.Single(approvals.Requests);
        Assert.Equal(now.AddDays(-1).Date.AddHours(18), filed.EventAt);
    }

    [Fact]
    public async Task SubmitTimeAdjustment_AcceptsPullingAnOvernightShiftBackToTheSameEvening()
    {
        // The reported case, in UTC as the wire carries it: clocked in 15 Sept
        // 06:04, forgot to clock out, closed it 16 Sept 08:26 — and asked for
        // the end to be 15 Sept 10:00 (6 PM local) instead. The correction is
        // earlier than the recorded clock-out and on an earlier day than it,
        // which is exactly what the date field exists to express.
        var timeIn = new DateTime(2026, 9, 15, 6, 4, 37, DateTimeKind.Utc);
        var timeOut = new DateTime(2026, 9, 16, 8, 26, 56, DateTimeKind.Utc);
        var (service, approvals) = AdjustmentService(timeIn: timeIn, timeOut: timeOut);

        var result = await service.SubmitTimeAdjustmentAsync("emp-1", new SubmitTimeAdjustmentDto
        {
            RecordId = "rec-1",
            RequestedTimeOut = new DateTime(2026, 9, 15, 10, 0, 0, DateTimeKind.Utc),
            Reason = "Left site at 6pm; forgot to clock out.",
        });

        Assert.True(result.Ok);
        var filed = Assert.Single(approvals.Requests);
        Assert.Equal(AttendanceApprovalKind.CLOCK_OUT, filed.Kind);
        Assert.Equal(new DateTime(2026, 9, 15, 10, 0, 0, DateTimeKind.Utc), filed.EventAt);
        // The original is kept so an approver can see what is being changed.
        Assert.Equal(timeOut, filed.OriginalEventAt);
        Assert.Equal(AttendanceApprovalStatus.PENDING, filed.ApprovalStatus);
    }

    [Fact]
    public async Task SubmitTimeAdjustment_RefusesAClockOutBeforeItsOwnClockIn()
    {
        // The wrong day picked: 18:00 two days ago, against a clock-in
        // yesterday. Hours would come out negative.
        var now = new DateTime(2026, 9, 16, 9, 0, 0, DateTimeKind.Utc);
        var (service, _) = AdjustmentService(
            timeIn: now.AddDays(-1).Date.AddHours(8), timeOut: now);

        var result = await service.SubmitTimeAdjustmentAsync("emp-1", new SubmitTimeAdjustmentDto
        {
            RecordId = "rec-1",
            RequestedTimeOut = now.AddDays(-2).Date.AddHours(18),
            Reason = "Wrong day picked by mistake.",
        });

        Assert.False(result.Ok);
        Assert.Contains("after the clock-in", result.Error);
    }

    [Fact]
    public async Task SubmitTimeAdjustment_RefusesAClockOutEqualToTheClockIn()
    {
        var now = new DateTime(2026, 9, 16, 9, 0, 0, DateTimeKind.Utc);
        var timeIn = now.AddDays(-1).Date.AddHours(8);
        var (service, _) = AdjustmentService(timeIn: timeIn, timeOut: now);

        var result = await service.SubmitTimeAdjustmentAsync("emp-1", new SubmitTimeAdjustmentDto
        {
            RecordId = "rec-1",
            RequestedTimeOut = timeIn,
            Reason = "Zero-length shift.",
        });

        Assert.False(result.Ok);
    }

    [Fact]
    public async Task SubmitTimeAdjustment_RefusesAClockInAfterTheClockOut()
    {
        // The mirror case, on a record that is already closed.
        var now = new DateTime(2026, 9, 16, 9, 0, 0, DateTimeKind.Utc);
        var (service, _) = AdjustmentService(timeIn: now.AddHours(-8), timeOut: now.AddHours(-1));

        var result = await service.SubmitTimeAdjustmentAsync("emp-1", new SubmitTimeAdjustmentDto
        {
            RecordId = "rec-1",
            RequestedTimeIn = now,
            Reason = "Started later than recorded.",
        });

        Assert.False(result.Ok);
        Assert.Contains("before the clock-out", result.Error);
    }

    private static (AttendanceService Service, FakeAttendanceApprovalRequestRepository Approvals)
        AdjustmentService(DateTime timeIn, DateTime timeOut)
    {
        var record = new AttendanceRecord
        {
            Id = "rec-1",
            EmployeeId = "emp-1",
            Date = timeIn.Date,
            TimeIn = timeIn,
            TimeOut = timeOut,
            ProjectId = null,
            Status = AttendanceStatus.CLOCKED_OUT,
            CreatedAt = timeIn,
            UpdatedAt = timeOut,
        };

        var approvals = new FakeAttendanceApprovalRequestRepository([]);

        var service = new AttendanceService(
            repo: new FakeAttendanceRepository([record]),
            sessions: new FakeAttendanceSessionRepository([]),
            breaks: new FakeAttendanceBreakRepository(),
            approvalRequests: approvals,
            projects: new FakeProjectService(),
            organizations: new FakeOrganizationService(),
            shifts: new FakeShiftService(),
            currentUser: new FakeCurrentUser(),
            photos: new FakeAttendancePhotoStorage(),
            policies: new FakePolicyService(),
            supervision: new FakeSupervisionService(),
            router: new FakeApprovalRouter(new() { ["emp-1"] = [["sup-1"]] }),
            directory: TestDirectory.Over(new FakeOrganizationMembershipRepository()),
            realtime: new FakeRealtimeService(),
            notifications: new FakeNotificationService(),
            employees: new FakeEmployeeDirectory(),
            hours: new FakeHoursSummaryService(),
            teams: new FakeTeamService(),
            holidays: new FakeHolidayService(),
            xero: new StubXeroForAttendance(),
            leave: new AltomateHR.Api.Tests.Payroll.StubPayrollLeave(),
            leaveTypes: new FakeLeaveTypeService());

        return (service, approvals);
    }

    // ---- Photo access ----
    //
    // Only the owner or an admin could open a clock-in/out photo, so every
    // supervisor's photo button failed. Anyone who oversees the employee — a
    // team they supervise, or any step of the approval chain — may now open it;
    // someone unrelated still may not.

    private static AttendanceService PhotoService(FakeTeamService? teams = null)
    {
        var record = new AttendanceRecord
        {
            Id = "rec-1",
            EmployeeId = "emp-1",
            Date = DateTime.UtcNow.Date,
            ClockOutPhotoUrl = "/attendance/photos/out.jpg",
            Status = AttendanceStatus.CLOCKED_OUT,
        };

        return new AttendanceService(
            repo: new FakeAttendanceRepository([record]),
            sessions: new FakeAttendanceSessionRepository([]),
            breaks: new FakeAttendanceBreakRepository(),
            approvalRequests: new FakeAttendanceApprovalRequestRepository([]),
            projects: new FakeProjectService(),
            organizations: new FakeOrganizationService(),
            shifts: new FakeShiftService(),
            currentUser: new FakeCurrentUser(),
            photos: new FakeAttendancePhotoStorage(),
            policies: new FakePolicyService(),
            supervision: new FakeSupervisionService(),
            // Two layers: sup-1 decides first, mgr-1 above them.
            router: new FakeApprovalRouter(new() { ["emp-1"] = [["sup-1"], ["mgr-1"]] }),
            directory: TestDirectory.Over(new FakeOrganizationMembershipRepository()),
            realtime: new FakeRealtimeService(),
            notifications: new FakeNotificationService(),
            employees: new FakeEmployeeDirectory(),
            hours: new FakeHoursSummaryService(),
            teams: teams ?? new FakeTeamService(),
            holidays: new FakeHolidayService(),
            xero: new StubXeroForAttendance(),
            leave: new AltomateHR.Api.Tests.Payroll.StubPayrollLeave(),
            leaveTypes: new FakeLeaveTypeService());
    }

    [Theory]
    [InlineData("emp-1", false)]   // the employee themselves
    [InlineData("usr-admin", true)] // an admin
    [InlineData("sup-1", false)]   // first approval step
    [InlineData("mgr-1", false)]   // a layer above — oversees them, not their turn
    public async Task Photo_opens_for_the_owner_an_admin_and_their_approval_chain(string userId, bool isAdmin)
    {
        var photo = await PhotoService().GetPhotoForUserAsync("out.jpg", userId, isAdmin);

        Assert.NotNull(photo);
    }

    [Fact]
    public async Task Photo_opens_for_a_supervisor_of_a_team_the_employee_is_on()
    {
        var teams = new FakeTeamService
        {
            SupervisedBy = { ["lead-1"] = [new SupervisedTeamDto { TeamId = "t-1", MemberIds = ["emp-1"] }] },
        };

        var photo = await PhotoService(teams).GetPhotoForUserAsync("out.jpg", "lead-1", isAdmin: false);

        Assert.NotNull(photo);
    }

    [Fact]
    public async Task Photo_stays_hidden_from_someone_who_does_not_oversee_the_employee()
    {
        var teams = new FakeTeamService
        {
            SupervisedBy = { ["lead-2"] = [new SupervisedTeamDto { TeamId = "t-2", MemberIds = ["emp-9"] }] },
        };

        var photo = await PhotoService(teams).GetPhotoForUserAsync("out.jpg", "lead-2", isAdmin: false);

        Assert.Null(photo);
    }

    // ---- Export scope ----
    //
    // Who may pull whose hours. The export used to be Admin/Owner only, so an
    // employee could not get their own record out — the commonest reason
    // anyone wants the file. Opening it up means the rule has to say precisely
    // where "your own hours" stops, because the same endpoint still serves the
    // whole org.

    private const string Sup = "usr-sup";
    private const string Report = "usr-report";
    private const string Stranger = "usr-stranger";

    private static AttendanceService ScopeService(params string[] reports) =>
        BuildService([], teams: new FakeTeamService
        {
            ReportsOf = new() { [Sup] = [.. reports] },
        });

    [Fact]
    public async Task ExportScope_Mine_ScopesToTheCallerWhoeverTheyAre()
    {
        var scope = await ScopeService().ResolveExportScopeAsync(
            Sup, isAdmin: false, employeeId: null, teamId: null, mine: true, team: false);

        Assert.True(scope.Allowed);
        Assert.Equal(Sup, scope.EmployeeId);
        // A team id smuggled alongside `mine` must not widen it.
        Assert.Null(scope.TeamId);
        Assert.Null(scope.EmployeeIds);
    }

    [Fact]
    public async Task ExportScope_Employee_MayNotPullSomeoneElsesRecord()
    {
        var scope = await ScopeService().ResolveExportScopeAsync(
            Stranger, isAdmin: false, employeeId: Report, teamId: null, mine: false, team: false);

        Assert.False(scope.Allowed);
    }

    [Fact]
    public async Task ExportScope_Employee_MayNotPullTheOrgByOmittingAnEmployee()
    {
        // No employeeId, no team — the unbounded request, and the one thing
        // the admin-only attribute used to be the sole guard against.
        var scope = await ScopeService().ResolveExportScopeAsync(
            Stranger, isAdmin: false, employeeId: null, teamId: null, mine: false, team: false);

        Assert.False(scope.Allowed);
    }

    [Fact]
    public async Task ExportScope_Supervisor_MayPullOneOfTheirReports()
    {
        var scope = await ScopeService(Report).ResolveExportScopeAsync(
            Sup, isAdmin: false, employeeId: Report, teamId: null, mine: false, team: false);

        Assert.True(scope.Allowed);
        Assert.Equal(Report, scope.EmployeeId);
    }

    [Fact]
    public async Task ExportScope_Supervisor_MayNotPullSomeoneOutsideTheirTeam()
    {
        var scope = await ScopeService(Report).ResolveExportScopeAsync(
            Sup, isAdmin: false, employeeId: Stranger, teamId: null, mine: false, team: false);

        Assert.False(scope.Allowed);
    }

    [Fact]
    public async Task ExportScope_Team_CoversTheSupervisorsReportsAndThemselves()
    {
        var scope = await ScopeService(Report).ResolveExportScopeAsync(
            Sup, isAdmin: false, employeeId: null, teamId: null, mine: false, team: true);

        Assert.True(scope.Allowed);
        Assert.NotNull(scope.EmployeeIds);
        // Themselves included: a supervisor's own hours belong in their team's
        // report, as they did in the previous system's team PDF.
        Assert.Equal([Sup, Report], scope.EmployeeIds!.ToArray());
        Assert.Null(scope.EmployeeId);
    }

    [Fact]
    public async Task ExportScope_Team_GivesAnAdminTheWholeOrg()
    {
        // Null EmployeeIds means "do not narrow".
        var scope = await ScopeService().ResolveExportScopeAsync(
            "usr-admin", isAdmin: true, employeeId: null, teamId: null, mine: false, team: true);

        Assert.True(scope.Allowed);
        Assert.Null(scope.EmployeeIds);
        Assert.Null(scope.EmployeeId);
    }

    [Fact]
    public async Task ExportScope_Supervisor_MayNotUseATeamIdToReachOutsideTheirReports()
    {
        // teamId is an admin filter: a supervisor cannot be checked against an
        // arbitrary team, so naming one is refused rather than quietly dropped.
        var scope = await ScopeService(Report).ResolveExportScopeAsync(
            Sup, isAdmin: false, employeeId: Report, teamId: "team-other", mine: false, team: false);

        Assert.False(scope.Allowed);
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
        // Photo-access tests only care WHETHER the file is handed over.
        public Task<AttendancePhotoFileResult?> GetAsync(string fileName) =>
            Task.FromResult<AttendancePhotoFileResult?>(new($"/tmp/{fileName}", "image/jpeg", fileName));
        public Task<bool> DeleteAsync(string fileName) => throw new NotImplementedException();
    }

    // Never reached — the record has no project, so the geofence check short-circuits.
    private sealed class FakeProjectService : IProjectService
    {
        // Read by the attendance clock-in gate; nothing under test here uses them.
        public Task<IReadOnlyList<ProjectGeofencePoint>> GetGeofencePointsAsync(string projectId) =>
            Task.FromResult<IReadOnlyList<ProjectGeofencePoint>>([]);
        public Task<IReadOnlyList<ProjectAllowedIp>> GetAllowedIpsAsync(string projectId) =>
            Task.FromResult<IReadOnlyList<ProjectAllowedIp>>([]);

        public Task<IEnumerable<ProjectDto>> GetForMemberAsync(string userId) =>
            throw new NotSupportedException();
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
    
    // Module access is not what these tests are about; full access keeps them
    // on topic.
    public Task<AltomateHR.Api.Modules.Policies.PolicyModuleAccess> GetModuleAccessAsync(string employeeId) =>
        Task.FromResult(AltomateHR.Api.Modules.Policies.PolicyModuleAccess.All);
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
        // Teams each user supervises. Empty unless a test is about team access.
        public Dictionary<string, List<SupervisedTeamDto>> SupervisedBy { get; init; } = [];

        public Task<IReadOnlyList<SupervisedTeamDto>> GetSupervisedTeamsAsync(string userId) =>
            Task.FromResult<IReadOnlyList<SupervisedTeamDto>>(SupervisedBy.GetValueOrDefault(userId, []));
        // Who reports to whom. Empty for every test that doesn't care, which
        // is all of them except the export-scope ones.
        public Dictionary<string, List<string>> ReportsOf { get; init; } = [];

        public Task<IReadOnlyList<string>> GetReportEmployeeIdsAsync(string supervisorId) =>
            Task.FromResult<IReadOnlyList<string>>(ReportsOf.GetValueOrDefault(supervisorId, []));

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

// Attendance now reads Xero-hosted clock photos back through a proxy. None of
// these tests exercise that, so the stub has no connection — which is also what
// an org without Xero looks like.
internal sealed class StubXeroForAttendance : AltomateHR.Api.Modules.Xero.IXeroFileReader
{
    public Task<AltomateHR.Api.Modules.Xero.XeroFileContent?> GetFileContentAsync(string fileId) =>
        Task.FromResult<AltomateHR.Api.Modules.Xero.XeroFileContent?>(null);
}
