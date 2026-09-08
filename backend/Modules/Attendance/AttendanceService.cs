using AltomateHR.Api.Modules.Shifts;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Attendance.Dtos;
using AltomateHR.Api.Modules.Attendance.Entities;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Notifications;
using AltomateHR.Api.Modules.Notifications.Entities;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Policies;
using AltomateHR.Api.Modules.Policies.Entities;
using AltomateHR.Api.Modules.Projects;
using AltomateHR.Api.Common.Tabular;
using AltomateHR.Api.Modules.Realtime;
using AltomateHR.Api.Modules.Realtime.Dtos;
using AltomateHR.Api.Modules.Teams;

namespace AltomateHR.Api.Modules.Attendance;

// Business logic: clock-in / clock-out + reads. One record per employee per
// local business day. Geofence enforcement: clocking against a project that has
// a geofence centre, from outside the org radius (or with no GPS at all),
// requires BOTH a remark and a photo — matching the current AltomateHR.
//
// Approval lives entirely on AttendanceApprovalRequest — one row per event
// (clock-in, clock-out, break-start, break-end). See that entity's comment
// for why: a single mutable slot on the record/break would mean a later
// event silently overwrites an earlier event's already-decided approval.
public class AttendanceService : IAttendanceService
{
    private const ApprovalModule Module = ApprovalModule.ATTENDANCE;
    private const string OffSiteCode = "OFF_SITE_ACTION_REQUIRED";
    private const string OpenSessionCode = "OPEN_SESSION_REQUIRES_CLOCK_OUT";
    private const string NotOnProjectCode = "NOT_ON_PROJECT";
    private const string IpNotAllowedCode = "IP_NOT_ALLOWED";
    private const int MaxBulkIds = 200;

    private static readonly IReadOnlySet<AttendanceApprovalKind> RecordKinds =
        new HashSet<AttendanceApprovalKind> { AttendanceApprovalKind.CLOCK_IN, AttendanceApprovalKind.CLOCK_OUT };

    private static readonly IReadOnlySet<AttendanceApprovalKind> BreakKinds =
        new HashSet<AttendanceApprovalKind> { AttendanceApprovalKind.BREAK_START, AttendanceApprovalKind.BREAK_END };

    private static readonly IReadOnlySet<AttendanceApprovalKind> AllKinds =
        new HashSet<AttendanceApprovalKind>(RecordKinds.Concat(BreakKinds));

    private readonly IDirectoryService _directory;
    private readonly IAttendanceRepository _repo;
    private readonly IAttendanceSessionRepository _sessions;
    private readonly IAttendanceBreakRepository _breaks;
    private readonly IAttendanceApprovalRequestRepository _approvalRequests;
    private readonly IProjectService _projects;
    private readonly IOrganizationService _organizations;
    private readonly IShiftService _shifts;
    private readonly ICurrentUser _currentUser;
    private readonly IAttendancePhotoStorage _photos;
    private readonly IPolicyService _policies;
    private readonly ISupervisionService _supervision;
    private readonly IApprovalRouter _router;
    private readonly IRealtimeService _realtime;
    private readonly INotificationService _notifications;
    private readonly IEmployeeRowResolver _employees;
    private readonly IHoursSummaryService _hours;
    private readonly ITeamService _teams;

    public AttendanceService(
        IAttendanceRepository repo,
        IAttendanceSessionRepository sessions,
        IAttendanceBreakRepository breaks,
        IAttendanceApprovalRequestRepository approvalRequests,
        IProjectService projects,
        IOrganizationService organizations,
        IShiftService shifts,
        ICurrentUser currentUser,
        IAttendancePhotoStorage photos,
        IPolicyService policies,
        ISupervisionService supervision,
        IApprovalRouter router,
        IDirectoryService directory,
        IRealtimeService realtime,
        INotificationService notifications,
        IEmployeeRowResolver employees,
        IHoursSummaryService hours,
        ITeamService teams)
    {
        _repo = repo;
        _teams = teams;
        _sessions = sessions;
        _breaks = breaks;
        _approvalRequests = approvalRequests;
        _projects = projects;
        _organizations = organizations;
        _shifts = shifts;
        _currentUser = currentUser;
        _photos = photos;
        _policies = policies;
        _supervision = supervision;
        _router = router;
        _directory = directory;
        _realtime = realtime;
        _notifications = notifications;
        _employees = employees;
        _hours = hours;
    }

    // The day, with its stints. Used wherever the caller is looking at a DAY
    // rather than a queue — the roll-up alone can't say a second shift began.
    private async Task<AttendanceRecordDto> ToDayDtoAsync(
        AttendanceRecord record, IReadOnlyList<AttendanceApprovalRequest> approvals) =>
        ToDto(record, approvals, await _sessions.GetByRecordAsync(record.Id));

    public async Task<AttendanceRecordDto?> GetTodayAsync(string employeeId)
    {
        var today = AttendanceTime.StartOfLocalDay(DateTime.UtcNow);
        var record = await _repo.GetForEmployeeOnDateAsync(employeeId, today);
        if (record is null) return null;
        var approvals = await _approvalRequests.GetByRecordIdsAsync([record.Id]);
        return await ToDayDtoAsync(record, approvals);
    }

    public async Task<AttendanceRecordDto?> GetOpenSessionAsync(string employeeId)
    {
        var open = await _repo.GetOpenForEmployeeAsync(employeeId);
        if (open is null) return null;

        var approvals = await _approvalRequests.GetByRecordIdsAsync([open.Id]);
        return await ToDayDtoAsync(open, approvals);
    }

    public async Task<IEnumerable<AttendanceRecordDto>> GetHistoryAsync(string userId, bool isAdmin)
    {
        var records = isAdmin
            ? await _repo.GetAllAsync()
            : await _repo.GetByEmployeeAsync(userId);
        var ids = records.Select(r => r.Id).ToList();

        var approvals = await _approvalRequests.GetByRecordIdsAsync(ids);
        var byRecord = approvals.GroupBy(a => a.AttendanceRecordId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<AttendanceApprovalRequest>)g.ToList());

        // Bulk, not per record: a year of history would otherwise be a query a day.
        var sessions = (await _sessions.GetByRecordIdsAsync(ids))
            .GroupBy(x => x.AttendanceRecordId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<AttendanceSession>)g.ToList());

        return records.Select(r =>
            ToDto(r, byRecord.GetValueOrDefault(r.Id, []), sessions.GetValueOrDefault(r.Id, [])));
    }

    // The RECORDS awaiting the caller as current-step approver — the day, its
    // clock times, GPS and photos, with the full approval history attached.
    //
    // It returned bare approval requests for a while, which is the wrong shape
    // for its only caller: a supervisor decides a SHIFT, and the request rows
    // carry none of what that decision needs to be informed — no clock times, no
    // coordinates, no proof photos. The approvals screen reads all of those off
    // the record, so it was reading undefined.
    public async Task<IEnumerable<AttendanceRecordDto>> GetTeamApprovalsAsync(string userId)
    {
        var pending = await _approvalRequests.GetOpenByKindsAsync(RecordKinds);
        var mine = new List<AttendanceApprovalRequest>();
        foreach (var request in pending)
        {
            var approvers = await _router.CurrentApproversAsync(Module, request.EmployeeId, request.CurrentStep);
            if (approvers.Contains(userId)) mine.Add(request);
        }
        if (mine.Count == 0) return [];

        var recordIds = mine.Select(r => r.AttendanceRecordId).Distinct().ToList();
        var records = await _repo.GetByIdsAsync(recordIds);

        // Every request on those records, not just the ones awaiting this
        // approver: the card shows the day's whole timeline, including what an
        // earlier step already decided.
        var approvals = (await _approvalRequests.GetByRecordIdsAsync(recordIds))
            .GroupBy(a => a.AttendanceRecordId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<AttendanceApprovalRequest>)g.ToList());

        var emails = await _supervision.GetEmailsAsync(records.Select(r => r.EmployeeId).Distinct());
        var dtos = records
            .OrderByDescending(r => r.Date)
            .Select(r => ToDto(r, approvals.GetValueOrDefault(r.Id, [])))
            .ToList();
        foreach (var dto in dtos) dto.EmployeeEmail = emails.GetValueOrDefault(dto.EmployeeId);
        return dtos;
    }

    // Today's attendance for the teams the caller oversees, grouped by project.
    //
    // Driven by TEAM MEMBERSHIP, not the supervisor field: a supervisor running
    // crews on two sites belongs to a team in each, and switching site is how
    // they actually look at their people. The supervisor field only ever gives a
    // single flat list, which can't be split by project at all.
    //
    // Distinct from GetTeamApprovalsAsync, which answers "what needs my
    // decision". This answers "where is my team right now".
    //
    // Returns every project at once rather than taking a projectId. It's one
    // day's rows for a handful of people, so switching tabs is instant instead
    // of a round trip each time.
    public async Task<IEnumerable<TeamAttendanceMemberDto>> GetTeamTodayAsync(string userId)
    {
        var supervised = await _teams.GetSupervisedTeamsAsync(userId);
        if (supervised.Count == 0) return [];

        var employeeIds = supervised.SelectMany(t => t.MemberIds).Distinct().ToList();
        var today = AttendanceTime.StartOfLocalDay(DateTime.UtcNow);
        var records = await _repo.GetForEmployeesOnDateAsync(employeeIds, today);
        var byEmployee = records.ToDictionary(r => r.EmployeeId);

        var approvals = (await _approvalRequests.GetByRecordIdsAsync(records.Select(r => r.Id)))
            .GroupBy(a => a.AttendanceRecordId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<AttendanceApprovalRequest>)g.ToList());

        var emails = await _supervision.GetEmailsAsync(employeeIds);
        var projectNames = (await _projects.GetAllAsync()).ToDictionary(p => p.Id, p => p.Name);

        return supervised
            .SelectMany(team => team.MemberIds.Select(id =>
            {
                var record = byEmployee.GetValueOrDefault(id);
                return new TeamAttendanceMemberDto
                {
                    EmployeeId = id,
                    EmployeeEmail = emails.GetValueOrDefault(id),
                    ProjectId = team.ProjectId,
                    ProjectName = projectNames.GetValueOrDefault(team.ProjectId),
                    TeamId = team.TeamId,
                    TeamName = team.TeamName,
                    Record = record is null ? null : ToDto(record, approvals.GetValueOrDefault(record.Id, [])),
                };
            }))
            .OrderBy(m => m.ProjectName)
            .ThenBy(m => m.EmployeeEmail)
            .ToList();
    }

    // Membership in any team belonging to that project.
    private async Task<bool> OnProjectAsync(string employeeId, string projectId) =>
        (await _teams.GetProjectIdsForMemberAsync(employeeId)).Contains(projectId);

    // Recompute the DAY from its sessions.
    //
    // The record is a roll-up, not a second source of truth: first start, last
    // end, and the SUM of each stint. Summing is the point — clock out at 12:00
    // and back in at 13:00 and the hour away is not worked time, which
    // last-minus-first would silently count.
    //
    // The day's clock-in evidence is the FIRST session's and its clock-out
    // evidence the LAST closed one's, so a second shift never overwrites the
    // morning's GPS or proof photo.
    private async Task RecomputeRollupAsync(AttendanceRecord record)
    {
        var sessions = await _sessions.GetByRecordAsync(record.Id);
        if (sessions.Count == 0) return;

        var first = sessions[0];
        var closed = sessions.Where(x => x.EndedAt is not null).ToList();
        var anyOpen = sessions.Any(x => x.EndedAt is null);

        record.TimeIn = first.StartedAt;
        record.TimeOut = anyOpen ? null : closed.Max(x => x.EndedAt);
        record.DurationMin = anyOpen ? null : closed.Sum(x => x.DurationMin ?? 0);

        // Lateness is the day's FIRST arrival. An afternoon session starting at
        // 14:00 is not "five hours late" against a 09:00 shift.
        record.LateByMin = first.LateByMin;

        // Status: still on the clock → the open session's own punctuality;
        // everything closed → the day is done.
        record.Status = anyOpen
            ? sessions.Last(x => x.EndedAt is null).Status
            : AttendanceStatus.CLOCKED_OUT;

        record.ClockInLat = first.ClockInLat;
        record.ClockInLng = first.ClockInLng;
        record.ClockInDistanceMeters = first.ClockInDistanceMeters;
        record.ClockInPhotoUrl = first.ClockInPhotoUrl;

        var last = closed.OrderBy(x => x.EndedAt).LastOrDefault();
        record.ClockOutLat = last?.ClockOutLat;
        record.ClockOutLng = last?.ClockOutLng;
        record.ClockOutDistanceMeters = last?.ClockOutDistanceMeters;
        record.ClockOutPhotoUrl = last?.ClockOutPhotoUrl;

        record.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(record);
    }

    public async Task<AttendanceActionResult> ClockInAsync(string employeeId, ClockInDto dto)
    {
        var now = DateTime.UtcNow;
        var today = AttendanceTime.StartOfLocalDay(now);
        var existing = await _repo.GetForEmployeeOnDateAsync(employeeId, today);

        // Only an OPEN stint blocks a new one. A finished shift doesn't end the
        // day: clocking out at noon and back in at one is two sessions on the
        // same record, which is what a split shift actually is.
        if (existing is not null && await _sessions.GetOpenForRecordAsync(existing.Id) is not null)
        {
            var currentApprovals = await _approvalRequests.GetByRecordIdsAsync([existing.Id]);
            return new AttendanceActionResult(false, await ToDayDtoAsync(existing, currentApprovals),
                "You're already clocked in.");
        }

        // A shift left open on an EARLIER day blocks clocking in today. Two open
        // sessions can't both be right, and letting a second one start is how a
        // forgotten clock-out becomes permanent — nothing ever forces the first
        // one to be resolved. Auto-clock-out only closes these when the
        // employee's policy opts in, so without this guard the rest just
        // accumulate.
        var openSession = await _repo.GetOpenForEmployeeAsync(employeeId);
        if (openSession is not null && openSession.Date != today)
        {
            var openApprovals = await _approvalRequests.GetByRecordIdsAsync([openSession.Id]);
            return new AttendanceActionResult(
                false,
                ToDto(openSession, openApprovals),
                "You're still clocked in from an earlier shift. Clock out of that one first — "
                + "you can request a time correction on it afterwards.",
                OpenSessionCode);
        }

        var effectiveProjectId = dto.ProjectId ?? existing?.ProjectId;

        // You can only clock into a project you're actually on.
        //
        // The picker used to list every project in the org and the server took
        // whatever it was handed, so anyone could tag their day to a site they
        // have no part in — which then shows them on that site's team view and
        // puts their hours against its costs. Membership is the same thing that
        // decides whose team you appear in, so the two can't disagree.
        //
        // Only checked when a project is named: attendance without a project is
        // still valid, and it's what someone with no team assignment gets.
        if (effectiveProjectId is not null && !await OnProjectAsync(employeeId, effectiveProjectId))
            return new AttendanceActionResult(false, null,
                "You're not assigned to that project. Pick one of your own, or ask an admin to add you to its team.",
                NotOnProjectCode);

        var policy = await _policies.GetEffectivePolicyAsync(employeeId);

        if (!await IpAllowedAsync(employeeId, effectiveProjectId, policy))
            return IpNotAllowed();

        var (_, distance, offSite) = await EvaluateGeofenceAsync(employeeId, effectiveProjectId, dto.Lat, dto.Lng);
        if (offSite && OffSiteProofMissing(dto.Remark, dto.PhotoUrl))
            return OffSiteRequired(distance);

        var (capturedLat, capturedLng) =
            CaptureCoords(policy, policy?.CaptureLocationOnClockIn ?? true, dto.Lat, dto.Lng);

        var lateByMin = AttendanceLateness.Minutes(now, await ScheduledStartAsync(employeeId));

        // The DAY. Only day-level facts are set here — the clock itself belongs
        // to the session, and RecomputeRollupAsync copies the summary back up.
        AttendanceRecord record;
        if (existing is null)
        {
            record = await _repo.AddAsync(new AttendanceRecord
            {
                EmployeeId = employeeId,
                Date = today,
                ProjectId = effectiveProjectId,
                Location = dto.Location,
                Remark = dto.Remark,
                Status = AttendanceStatus.CLOCKED_IN,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
        else
        {
            // Either a pre-seeded MISSING/ON_LEAVE day with no clock yet, or a
            // day whose earlier shift is already closed. Both just gain a
            // session; the unique key means there's only ever one row per day.
            existing.ProjectId = effectiveProjectId ?? existing.ProjectId;
            existing.Location = dto.Location ?? existing.Location;
            existing.Remark = dto.Remark ?? existing.Remark;
            existing.UpdatedAt = now;
            await _repo.UpdateAsync(existing);
            record = existing;
        }

        // This stint, with its own evidence and its own punctuality.
        var session = await _sessions.AddAsync(new AttendanceSession
        {
            AttendanceRecordId = record.Id,
            EmployeeId = employeeId,
            StartedAt = now,
            Status = AttendanceStatus.CLOCKED_IN,
            LateByMin = lateByMin,
            ClockInLat = capturedLat,
            ClockInLng = capturedLng,
            ClockInDistanceMeters = distance,
            ClockInPhotoUrl = dto.PhotoUrl,
            CreatedAt = now,
            UpdatedAt = now,
        });

        await RecomputeRollupAsync(record);

        var request = await FileRequestAsync(new AttendanceApprovalRequest
        {
            EmployeeId = employeeId,
            Kind = AttendanceApprovalKind.CLOCK_IN,
            AttendanceRecordId = record.Id,
            AttendanceSessionId = session.Id,
            EventAt = now,
            SubmittedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        });

        await NotifyPendingAsync(request, RealtimeAction.SUBMITTED);
        return new AttendanceActionResult(true, await ToDayDtoAsync(record, [request]));
    }

    public async Task<AttendanceActionResult> ClockOutAsync(string employeeId, ClockOutDto dto)
    {
        var now = DateTime.UtcNow;
        var today = AttendanceTime.StartOfLocalDay(now);

        // Whichever day the open session belongs to. Scoping this to today would
        // strand anyone who forgot to clock out: clock-in refuses because a
        // session is open, and clock-out refuses because it isn't today's.
        var record = await _repo.GetOpenForEmployeeAsync(employeeId)
                     ?? await _repo.GetForEmployeeOnDateAsync(employeeId, today);

        if (record is null || record.TimeIn is null)
            return new AttendanceActionResult(false, null, "You haven't clocked in today.");

        if (await _sessions.GetOpenForRecordAsync(record.Id) is null)
        {
            var currentApprovals = await _approvalRequests.GetByRecordIdsAsync([record.Id]);
            return new AttendanceActionResult(false, ToDto(record, currentApprovals),
                "You're not clocked in right now.");
        }

        var policy = await _policies.GetEffectivePolicyAsync(employeeId);

        if (!await IpAllowedAsync(employeeId, record.ProjectId, policy))
            return IpNotAllowed();

        var (_, distance, offSite) = await EvaluateGeofenceAsync(employeeId, record.ProjectId, dto.Lat, dto.Lng);
        if (offSite && OffSiteProofMissing(dto.Remark, dto.PhotoUrl))
            return OffSiteRequired(distance);

        var (capturedLat, capturedLng) =
            CaptureCoords(policy, policy?.CaptureLocationOnClockOut ?? true, dto.Lat, dto.Lng);

        // Close THIS stint. The day's totals follow from its sessions.
        var session = await _sessions.GetOpenForRecordAsync(record.Id);
        if (session is not null)
        {
            session.EndedAt = now;
            session.DurationMin = (int)Math.Round((now - session.StartedAt).TotalMinutes);
            session.Status = AttendanceStatus.CLOCKED_OUT;
            session.ClockOutLat = capturedLat;
            session.ClockOutLng = capturedLng;
            session.ClockOutDistanceMeters = distance;
            session.ClockOutPhotoUrl = dto.PhotoUrl;
            session.UpdatedAt = now;
            await _sessions.UpdateAsync(session);
        }

        if (!string.IsNullOrWhiteSpace(dto.Remark)) record.Remark = dto.Remark;
        await RecomputeRollupAsync(record);

        var clockOutRequest = await FileRequestAsync(new AttendanceApprovalRequest
        {
            EmployeeId = employeeId,
            Kind = AttendanceApprovalKind.CLOCK_OUT,
            AttendanceRecordId = record.Id,
            AttendanceSessionId = session?.Id,
            EventAt = now,
            SubmittedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await NotifyPendingAsync(clockOutRequest, RealtimeAction.SUBMITTED);

        var allApprovals = await _approvalRequests.GetByRecordIdsAsync([record.Id]);
        return new AttendanceActionResult(true, await ToDayDtoAsync(record, allApprovals));
    }

    public async Task<AttendanceTransitionResult> ApproveAsync(string id, string approverId)
    {
        var (request, found, error) = await LoadDecidableRequestAsync(id, approverId, RecordKinds);
        if (!found) return new AttendanceTransitionResult(false, false, null);
        if (error is not null)
            return new AttendanceTransitionResult(true, false, await ToRecordDtoAsync(request!), error);

        await DecideAsync(request!, approverId, approve: true, reviewNotes: null);
        return new AttendanceTransitionResult(true, true, await ToRecordDtoAsync(request!));
    }

    public async Task<AttendanceTransitionResult> RejectAsync(string id, string approverId, string? reviewNotes)
    {
        var (request, found, error) = await LoadDecidableRequestAsync(id, approverId, RecordKinds);
        if (!found) return new AttendanceTransitionResult(false, false, null);
        if (error is not null)
            return new AttendanceTransitionResult(true, false, await ToRecordDtoAsync(request!), error);

        var cleanedReviewNotes = Clean(reviewNotes);
        if (cleanedReviewNotes is null)
            return new AttendanceTransitionResult(true, false, await ToRecordDtoAsync(request!),
                "Enter a rejection remark before rejecting this attendance record.");

        await DecideAsync(request!, approverId, approve: false, reviewNotes: cleanedReviewNotes);
        return new AttendanceTransitionResult(true, true, await ToRecordDtoAsync(request!));
    }

    public async Task<AttendanceBreakActionResult> StartBreakAsync(string employeeId, StartBreakDto dto)
    {
        var now = DateTime.UtcNow;
        var today = AttendanceTime.StartOfLocalDay(now);
        var record = await _repo.GetForEmployeeOnDateAsync(employeeId, today);
        if (record is null || record.TimeIn is null || record.TimeOut is not null)
            return new AttendanceBreakActionResult(false, null, "Clock in before starting a break.");

        // Distinct from the check above: the day IS open, but it has no session
        // to hang a break off. Reporting "clock in first" to someone who can see
        // they are clocked in sends them looking for the wrong problem.
        var session = await _sessions.GetOpenForRecordAsync(record.Id);
        if (session is null)
            return new AttendanceBreakActionResult(false, null,
                "This shift has no open work session, so a break can't be recorded against it. "
                + "Clock out and clock in again to start one.");

        var openBreak = await _breaks.GetOpenForSessionAsync(session.Id);
        if (openBreak is not null)
            return new AttendanceBreakActionResult(false, null, "You're already on break.");

        var policy = await _policies.GetEffectivePolicyAsync(employeeId);
        var captureGps = policy?.CaptureLocationOnBreakStart != false && dto.Lat is not null && dto.Lng is not null;

        var brk = new AttendanceBreak
        {
            AttendanceSessionId = session.Id,
            AttendanceRecordId = record.Id,
            EmployeeId = employeeId,
            StartedAt = now,
            StartLat = captureGps ? dto.Lat : null,
            StartLng = captureGps ? dto.Lng : null,
            Remark = Clean(dto.Remark),
            CreatedAt = now,
            UpdatedAt = now,
        };
        var saved = await _breaks.AddAsync(brk);

        var request = await FileRequestAsync(new AttendanceApprovalRequest
        {
            EmployeeId = employeeId,
            Kind = AttendanceApprovalKind.BREAK_START,
            AttendanceRecordId = record.Id,
            AttendanceSessionId = session.Id,
            AttendanceBreakId = saved.Id,
            EventAt = now,
            SubmittedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        });

        await NotifyPendingAsync(request, RealtimeAction.SUBMITTED);
        return new AttendanceBreakActionResult(true, ToBreakDto(saved, [request]));
    }

    public async Task<AttendanceBreakActionResult> EndBreakAsync(string employeeId, EndBreakDto dto)
    {
        var now = DateTime.UtcNow;
        var today = AttendanceTime.StartOfLocalDay(now);
        var record = await _repo.GetForEmployeeOnDateAsync(employeeId, today);
        if (record is null || record.TimeIn is null || record.TimeOut is not null)
            return new AttendanceBreakActionResult(false, null, "Start a break before ending one.");

        var session = await _sessions.GetOpenForRecordAsync(record.Id);
        if (session is null)
            return new AttendanceBreakActionResult(false, null, "Start a break before ending one.");

        var brk = await _breaks.GetOpenForSessionAsync(session.Id);
        if (brk is null)
            return new AttendanceBreakActionResult(false, null, "Start a break before ending one.");

        var policy = await _policies.GetEffectivePolicyAsync(employeeId);
        var captureGps = policy?.CaptureLocationOnBreakEnd != false && dto.Lat is not null && dto.Lng is not null;

        brk.EndedAt = now;
        brk.EndLat = captureGps ? dto.Lat : null;
        brk.EndLng = captureGps ? dto.Lng : null;
        if (!string.IsNullOrWhiteSpace(dto.Remark)) brk.Remark = dto.Remark;
        brk.UpdatedAt = now;
        await _breaks.UpdateAsync(brk);

        var breakEndRequest = await FileRequestAsync(new AttendanceApprovalRequest
        {
            EmployeeId = employeeId,
            Kind = AttendanceApprovalKind.BREAK_END,
            AttendanceRecordId = brk.AttendanceRecordId,
            AttendanceSessionId = brk.AttendanceSessionId,
            AttendanceBreakId = brk.Id,
            EventAt = now,
            SubmittedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await NotifyPendingAsync(breakEndRequest, RealtimeAction.SUBMITTED);

        var allApprovals = await _approvalRequests.GetByBreakIdsAsync([brk.Id]);
        return new AttendanceBreakActionResult(true, ToBreakDto(brk, allApprovals));
    }

    public async Task<AttendanceBreakTransitionResult> ApproveBreakAsync(string id, string approverId)
    {
        var (request, found, error) = await LoadDecidableRequestAsync(id, approverId, BreakKinds);
        if (!found) return new AttendanceBreakTransitionResult(false, false, null);
        if (error is not null)
            return new AttendanceBreakTransitionResult(true, false, await ToBreakDtoAsync(request!), error);

        await DecideAsync(request!, approverId, approve: true, reviewNotes: null);
        return new AttendanceBreakTransitionResult(true, true, await ToBreakDtoAsync(request!));
    }

    public async Task<AttendanceBreakTransitionResult> RejectBreakAsync(string id, string approverId, string? reviewNotes)
    {
        var (request, found, error) = await LoadDecidableRequestAsync(id, approverId, BreakKinds);
        if (!found) return new AttendanceBreakTransitionResult(false, false, null);
        if (error is not null)
            return new AttendanceBreakTransitionResult(true, false, await ToBreakDtoAsync(request!), error);

        var cleanedReviewNotes = Clean(reviewNotes);
        if (cleanedReviewNotes is null)
            return new AttendanceBreakTransitionResult(true, false, await ToBreakDtoAsync(request!),
                "Enter a rejection remark before rejecting this break.");

        await DecideAsync(request!, approverId, approve: false, reviewNotes: cleanedReviewNotes);
        return new AttendanceBreakTransitionResult(true, true, await ToBreakDtoAsync(request!));
    }

    public async Task<IEnumerable<AttendanceApprovalRequestDto>> GetTeamBreakApprovalsAsync(string userId)
    {
        var pending = await _approvalRequests.GetOpenByKindsAsync(BreakKinds);
        var visible = new List<AttendanceApprovalRequest>();
        foreach (var request in pending)
        {
            var approvers = await _router.CurrentApproversAsync(Module, request.EmployeeId, request.CurrentStep);
            if (approvers.Contains(userId)) visible.Add(request);
        }

        var emails = await _supervision.GetEmailsAsync(visible.Select(r => r.EmployeeId).Distinct());
        return visible.Select(r => ToApprovalRequestDto(r, emails.GetValueOrDefault(r.EmployeeId)));
    }

    public async Task<AttendanceBreakListResult> GetBreaksForRecordAsync(
        string recordId,
        string requestingUserId,
        string? requestingRole)
    {
        var record = await _repo.GetByIdAsync(recordId);
        if (record is null)
            return new AttendanceBreakListResult(false, false, null);

        var authorized = requestingUserId == record.EmployeeId
            || await _supervision.CanApproveAsync(record.EmployeeId, requestingUserId, requestingRole);
        if (!authorized)
            return new AttendanceBreakListResult(true, false, null, "Not authorized to view this employee's breaks.");

        var breaks = await _breaks.GetByRecordAsync(recordId);
        var approvals = await _approvalRequests.GetByBreakIdsAsync(breaks.Select(b => b.Id));
        var byBreak = approvals.GroupBy(a => a.AttendanceBreakId!)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<AttendanceApprovalRequest>)g.ToList());
        return new AttendanceBreakListResult(true, true,
            breaks.Select(b => ToBreakDto(b, byBreak.GetValueOrDefault(b.Id, []))));
    }

    public async Task<AttendanceBulkResult> BulkApproveAsync(IReadOnlyList<string> ids, string approverId)
    {
        var overflow = ids.Count > MaxBulkIds;
        var toProcess = overflow ? [] : ids;
        var items = new List<AttendanceBulkResultItem>();
        var toSave = new List<AttendanceApprovalRequest>();

        if (overflow)
            items.Add(new AttendanceBulkResultItem(string.Empty, false, $"Too many ids — pick fewer than {MaxBulkIds}."));

        foreach (var id in toProcess)
        {
            var (request, found, error) = await LoadDecidableRequestAsync(id, approverId, AllKinds);
            if (!found) { items.Add(new AttendanceBulkResultItem(id, false, "Not found.")); continue; }
            if (error is not null) { items.Add(new AttendanceBulkResultItem(id, false, error)); continue; }

            await DecideInMemoryAsync(request!, approverId, approve: true, reviewNotes: null);
            toSave.Add(request!);
            items.Add(new AttendanceBulkResultItem(id, true));
        }

        if (toSave.Count > 0) await _approvalRequests.UpdateRangeAsync(toSave);
        return BuildBulkResult(items);
    }

    public async Task<AttendanceBulkResult> BulkRejectAsync(IReadOnlyList<string> ids, string approverId, string? reviewNotes)
    {
        var cleanedReviewNotes = Clean(reviewNotes);
        if (cleanedReviewNotes is null)
            return new AttendanceBulkResult(0, ids.Count,
                ids.Select(id => new AttendanceBulkResultItem(id, false, "Enter a rejection remark before rejecting.")).ToList());

        var overflow = ids.Count > MaxBulkIds;
        var toProcess = overflow ? [] : ids;
        var items = new List<AttendanceBulkResultItem>();
        var toSave = new List<AttendanceApprovalRequest>();

        if (overflow)
            items.Add(new AttendanceBulkResultItem(string.Empty, false, $"Too many ids — pick fewer than {MaxBulkIds}."));

        foreach (var id in toProcess)
        {
            var (request, found, error) = await LoadDecidableRequestAsync(id, approverId, AllKinds);
            if (!found) { items.Add(new AttendanceBulkResultItem(id, false, "Not found.")); continue; }
            if (error is not null) { items.Add(new AttendanceBulkResultItem(id, false, error)); continue; }

            await DecideInMemoryAsync(request!, approverId, approve: false, reviewNotes: cleanedReviewNotes);
            toSave.Add(request!);
            items.Add(new AttendanceBulkResultItem(id, true));
        }

        if (toSave.Count > 0) await _approvalRequests.UpdateRangeAsync(toSave);
        return BuildBulkResult(items);
    }

    public async Task<IEnumerable<AttendanceApprovalRequestDto>> GetAuditLogAsync(
        string? employeeId, DateTime? from, DateTime? to)
    {
        var requests = await _approvalRequests.GetForAuditAsync(employeeId, from, to);
        var emails = await _supervision.GetEmailsAsync(requests.Select(r => r.EmployeeId).Distinct());
        return requests.Select(r => ToApprovalRequestDto(r, emails.GetValueOrDefault(r.EmployeeId)));
    }

    public async Task<AttendanceSelfieStorageStatsDto> GetSelfieStorageStatsAsync()
    {
        var records = await _repo.GetWithPhotosAsync();
        var total = records.Count(r => r.ClockInPhotoUrl is not null)
            + records.Count(r => r.ClockOutPhotoUrl is not null);
        return new AttendanceSelfieStorageStatsDto
        {
            Total = total,
            Oldest = records.Count == 0 ? null : records.Min(r => r.Date).ToString("yyyy-MM-dd"),
            Newest = records.Count == 0 ? null : records.Max(r => r.Date).ToString("yyyy-MM-dd"),
        };
    }

    public async Task<AttendanceDeleteSelfiesResultDto> DeleteSelfiesInRangeAsync(DateTime from, DateTime to)
    {
        var records = await _repo.GetWithPhotosInRangeAsync(from, to);
        int scanned = 0, deleted = 0, failed = 0;

        foreach (var record in records)
        {
            var changed = false;

            if (record.ClockInPhotoUrl is not null)
            {
                scanned++;
                if (await TryDeletePhotoAsync(record.ClockInPhotoUrl))
                {
                    record.ClockInPhotoUrl = null;
                    changed = true;
                    deleted++;
                }
                else failed++;
            }

            if (record.ClockOutPhotoUrl is not null)
            {
                scanned++;
                if (await TryDeletePhotoAsync(record.ClockOutPhotoUrl))
                {
                    record.ClockOutPhotoUrl = null;
                    changed = true;
                    deleted++;
                }
                else failed++;
            }

            if (changed)
            {
                record.UpdatedAt = DateTime.UtcNow;
                await _repo.UpdateAsync(record);
            }
        }

        return new AttendanceDeleteSelfiesResultDto { Scanned = scanned, Deleted = deleted, Failed = failed };
    }

    private async Task<bool> TryDeletePhotoAsync(string photoUrl)
    {
        var fileName = Path.GetFileName(photoUrl);
        try
        {
            return await _photos.DeleteAsync(fileName);
        }
        catch
        {
            return false;
        }
    }

    public async Task<AttendanceAdjustmentResult> SubmitTimeAdjustmentAsync(string employeeId, SubmitTimeAdjustmentDto dto)
    {
        var reason = Clean(dto.Reason);
        if (reason is null)
            return new AttendanceAdjustmentResult(false, "Please add a reason for the adjustment.", []);

        if (dto.RequestedTimeIn is null && dto.RequestedTimeOut is null)
            return new AttendanceAdjustmentResult(false, "Enter at least one corrected time.", []);

        if (dto.RequestedTimeIn is not null && dto.RequestedTimeOut is not null
            && dto.RequestedTimeOut <= dto.RequestedTimeIn)
            return new AttendanceAdjustmentResult(false, "Clock-out must be after clock-in.", []);

        var record = await _repo.GetByIdAsync(dto.RecordId);
        if (record is null || record.EmployeeId != employeeId)
            return new AttendanceAdjustmentResult(false, "Attendance record not found.", []);

        var created = new List<AttendanceApprovalRequest>();
        string? firstError = null;

        if (dto.RequestedTimeIn is not null)
        {
            var (ok, error, request) = await UpsertAdjustmentRequestAsync(
                record, AttendanceApprovalKind.CLOCK_IN, record.TimeIn, dto.RequestedTimeIn.Value, employeeId, reason);
            if (ok) created.Add(request!); else firstError ??= error;
        }

        if (dto.RequestedTimeOut is not null)
        {
            var (ok, error, request) = await UpsertAdjustmentRequestAsync(
                record, AttendanceApprovalKind.CLOCK_OUT, record.TimeOut, dto.RequestedTimeOut.Value, employeeId, reason);
            if (ok) created.Add(request!); else firstError ??= error;
        }

        if (created.Count == 0)
            return new AttendanceAdjustmentResult(false, firstError ?? "Nothing to change.", []);

        return new AttendanceAdjustmentResult(true, null, created.Select(a => ToApprovalRequestDto(a, null)).ToList());
    }

    // At least one of clock-in/clock-out corrections must land; the other is
    // reported via Error but doesn't fail the whole submission (matches how
    // the reference app allows a partial adjustment when only one side is bad).
    private async Task<(bool Ok, string? Error, AttendanceApprovalRequest? Request)> UpsertAdjustmentRequestAsync(
        AttendanceRecord record,
        AttendanceApprovalKind kind,
        DateTime? originalAt,
        DateTime requestedAt,
        string employeeId,
        string reason)
    {
        if (originalAt is null)
            return (false, kind == AttendanceApprovalKind.CLOCK_IN
                ? "There's no clock-in to correct."
                : "There's no clock-out to correct.", null);

        if (originalAt.Value == requestedAt)
            return (false, "The requested time matches the current record — nothing to change.", null);

        var now = DateTime.UtcNow;

        // Reuse an existing PENDING adjustment of the same kind so a re-submission
        // edits it in place instead of stacking duplicates.
        var existingForRecord = await _approvalRequests.GetByRecordIdsAsync([record.Id]);
        var pendingAdjustment = existingForRecord.FirstOrDefault(a =>
            a.Kind == kind && a.ApprovalStatus == AttendanceApprovalStatus.PENDING && a.OriginalEventAt is not null);

        if (pendingAdjustment is not null)
        {
            pendingAdjustment.EventAt = requestedAt;
            pendingAdjustment.Reason = reason;
            pendingAdjustment.SubmittedAt = now;
            pendingAdjustment.UpdatedAt = now;
            await _approvalRequests.UpdateAsync(pendingAdjustment);
            return (true, null, pendingAdjustment);
        }

        var session = await _sessions.GetOpenForRecordAsync(record.Id);   // may be null once fully clocked out — fine, nullable
        var created = await FileRequestAsync(new AttendanceApprovalRequest
        {
            EmployeeId = employeeId,
            Kind = kind,
            AttendanceRecordId = record.Id,
            AttendanceSessionId = session?.Id,
            EventAt = requestedAt,
            OriginalEventAt = originalAt,
            Reason = reason,
            SubmittedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await NotifyPendingAsync(created, RealtimeAction.SUBMITTED);
        return (true, null, created);
    }

    // Per-policy auto clock-out. Opt-in: an employee is only swept when their
    // effective policy has AutoClockOutEnabled with a threshold set, so this is
    // a no-op until an admin configures it. Runs with no request context, so
    // policy/membership lookups here go through the org-explicit, filter-
    // bypassing repository methods rather than the "current org" ones.
    public async Task<AttendanceAutoClockOutResultDto> RunAutoClockOutSweepAsync(int maxCandidates)
    {
        var allPolicies = await _policies.GetAllAcrossOrgsAsync();
        var enabled = allPolicies
            .Where(p => p.AutoClockOutEnabled && p.AutoClockOutAfterMinutes is > 0)
            .ToList();

        // Nothing configured anywhere — skip the session query entirely.
        if (enabled.Count == 0)
            return new AttendanceAutoClockOutResultDto { Inspected = 0, ClockedOut = 0, Errors = 0 };

        var policyById = allPolicies.ToDictionary(p => p.Id);
        var defaultByOrg = allPolicies
            .Where(p => p.IsDefault)
            .GroupBy(p => p.OrganizationId)
            .ToDictionary(g => g.Key, g => g.First());

        // Widest net: anyone open longer than the shortest configured threshold
        // is a candidate; each is then re-checked against their own policy.
        var minThreshold = enabled.Min(p => p.AutoClockOutAfterMinutes!.Value);
        var candidates = await _sessions.GetOpenStartedBeforeAsync(
            DateTime.UtcNow.AddMinutes(-minThreshold), maxCandidates);

        var clockedOut = 0;
        var errors = 0;
        foreach (var session in candidates)
        {
            try
            {
                var record = await _repo.GetByIdAsync(session.AttendanceRecordId);
                if (record is null || record.TimeOut is not null) continue;   // already handled

                var policy = await ResolvePolicyForSweepAsync(
                    record.OrganizationId, record.EmployeeId, policyById, defaultByOrg);
                if (policy is null || !policy.AutoClockOutEnabled || policy.AutoClockOutAfterMinutes is not > 0)
                    continue;   // not opted in

                var cutoffMinutes = policy.AutoClockOutAfterMinutes.Value;
                var cutoffAt = session.StartedAt.AddMinutes(cutoffMinutes);
                if (cutoffAt > DateTime.UtcNow) continue;   // not past THIS employee's threshold yet

                var now = DateTime.UtcNow;

                session.EndedAt = cutoffAt;
                session.UpdatedAt = now;
                await _sessions.UpdateAsync(session);

                record.TimeOut = cutoffAt;
                record.DurationMin = (int)Math.Round((cutoffAt - (record.TimeIn ?? cutoffAt)).TotalMinutes);
                record.Status = AttendanceStatus.CLOCKED_OUT;
                record.Notes = string.IsNullOrEmpty(record.Notes)
                    ? "Auto clocked-out by system (forgot to clock out)."
                    : record.Notes + " | Auto clocked-out by system.";
                record.UpdatedAt = now;
                await _repo.UpdateAsync(record);

                // No request-context user to auto-stamp OrganizationId here
                // (StampTenant no-ops without a current org) — set explicitly.
                var autoRequest = await FileRequestAsync(new AttendanceApprovalRequest
                {
                    OrganizationId = record.OrganizationId,
                    EmployeeId = record.EmployeeId,
                    Kind = AttendanceApprovalKind.CLOCK_OUT,
                    AttendanceRecordId = record.Id,
                    AttendanceSessionId = session.Id,
                    EventAt = cutoffAt,
                    SubmittedAt = now,
                    CreatedAt = now,
                    UpdatedAt = now,
                });

                // The employee is long gone, but their approver may well have a
                // tab open — and this is a request they now have to review.
                await NotifyPendingAsync(autoRequest, RealtimeAction.SUBMITTED);

                clockedOut++;
            }
            catch
            {
                errors++;
            }
        }

        return new AttendanceAutoClockOutResultDto
        {
            Inspected = candidates.Count,
            ClockedOut = clockedOut,
            Errors = errors,
        };
    }

    public async Task<IEnumerable<StillClockedInWarningDto>> GetStillClockedInWarningsAsync(int thresholdMinutes)
    {
        var now = DateTime.UtcNow;
        var open = await _repo.GetOpenRecordsAsync();
        var warnings = open
            .Where(r => r.TimeIn is not null && (now - r.TimeIn.Value).TotalMinutes >= thresholdMinutes)
            .Select(r => new StillClockedInWarningDto
            {
                EmployeeId = r.EmployeeId,
                RecordId = r.Id,
                TimeIn = Iso(r.TimeIn) ?? string.Empty,
                MinutesClockedIn = (int)Math.Round((now - r.TimeIn!.Value).TotalMinutes),
            })
            .ToList();

        var emails = await _supervision.GetEmailsAsync(warnings.Select(w => w.EmployeeId).Distinct());
        foreach (var w in warnings) w.EmployeeEmail = emails.GetValueOrDefault(w.EmployeeId);
        return warnings;
    }

    public async Task<PendingApprovalDigestDto> GetPendingApprovalDigestAsync(string userId)
    {
        var pending = await _approvalRequests.GetOpenByKindsAsync(AllKinds);
        var mine = new List<AttendanceApprovalRequest>();
        foreach (var request in pending)
        {
            var approvers = await _router.CurrentApproversAsync(Module, request.EmployeeId, request.CurrentStep);
            if (approvers.Contains(userId)) mine.Add(request);
        }

        return new PendingApprovalDigestDto
        {
            PendingCount = mine.Count,
            OldestSubmittedAt = mine.Count == 0 ? null : Iso(mine.Min(r => r.SubmittedAt)),
        };
    }

    public async Task<IReadOnlyList<OrgApprovalDigestEntryDto>> GetOrgApprovalDigestAsync()
    {
        var pending = await _approvalRequests.GetOpenByKindsAsync(AllKinds);

        // Keyed by (reviewer, org) rather than reviewer alone: this runs with
        // no request context (like the Leave accrual sweep), so the tenant
        // filter is a no-op and it scans every org's pending rows at once —
        // the digest notification needs to know which org each count is for.
        var countByKey = new Dictionary<(string ReviewerId, string OrganizationId), int>();
        foreach (var request in pending)
        {
            var approvers = await _router.CurrentApproversAsync(Module, request.EmployeeId, request.CurrentStep);
            foreach (var reviewerId in approvers)
            {
                var key = (reviewerId, request.OrganizationId);
                countByKey[key] = countByKey.GetValueOrDefault(key) + 1;
            }
        }

        return countByKey
            .Select(kv => new OrgApprovalDigestEntryDto
            {
                ReviewerId = kv.Key.ReviewerId,
                OrganizationId = kv.Key.OrganizationId,
                PendingCount = kv.Value,
            })
            .ToList();
    }

    public Task<AttendancePhotoUploadResult> StorePhotoAsync(AttendancePhotoUpload upload) =>
        _photos.StoreAsync(upload);

    public async Task<AttendancePhotoFileResult?> GetPhotoForUserAsync(
        string fileName,
        string userId,
        bool isAdmin)
    {
        var photoUrl = $"/attendance/photos/{fileName}";
        var record = await _repo.GetByPhotoUrlAsync(photoUrl);
        if (record is null)
            return null;

        if (!isAdmin && record.EmployeeId != userId)
            return null;

        return await _photos.GetAsync(fileName);
    }

    // Background-safe policy resolution: mirrors PolicyService.GetEffectivePolicy
    // (assigned policy, else the org default) but takes the org explicitly and
    // reads from pre-fetched dictionaries, since the sweep has no request
    // context for the tenant filter to key off and spans every org.
    private async Task<EmployeePolicy?> ResolvePolicyForSweepAsync(
        string organizationId,
        string employeeId,
        IReadOnlyDictionary<string, EmployeePolicy> policyById,
        IReadOnlyDictionary<string, EmployeePolicy> defaultByOrg)
    {
        var membership = await _directory.GetMembershipAsync(organizationId, employeeId);
        if (membership?.PolicyId is not null && policyById.TryGetValue(membership.PolicyId, out var assigned))
            return assigned;
        return defaultByOrg.GetValueOrDefault(organizationId);
    }

    // Evaluate clock coords against a project's geofence.
    //   not geofenced (no project, or project has no centre) → never off-site.
    //   geofenced + no GPS → off-site (presence can't be verified).
    //   geofenced + GPS    → off-site when distance exceeds the org radius.
    private async Task<(bool Geofenced, double? Distance, bool OffSite)> EvaluateGeofenceAsync(
        string employeeId, string? projectId, double? lat, double? lng)
    {
        if (string.IsNullOrEmpty(projectId)) return (false, null, false);

        var project = await _projects.GetByIdAsync(projectId);
        if (project?.Latitude is null || project.Longitude is null) return (false, null, false);

        // Policy gate: an employee whose policy doesn't require the geofence
        // still has their distance captured, but is never flagged off-site.
        var enforce = await _policies.RequiresGeofenceAsync(employeeId);

        if (lat is null || lng is null) return (true, null, enforce);   // no GPS → off-site only when enforced

        var distance = Geo.HaversineMeters(lat.Value, lng.Value, project.Latitude.Value, project.Longitude.Value);
        var radius = await GetRadiusAsync();
        return (true, distance, enforce && distance > radius);
    }

    // Per-event GPS capture gate. GeolocationEnabled is the master switch; the
    // per-event flag only matters when it's on. Returns the coords to STORE —
    // suppressing capture never blocks the clock event, and never suppresses
    // the geofence decision itself (which still ran on the submitted coords).
    private static (double? Lat, double? Lng) CaptureCoords(
        EmployeePolicy? policy, bool perEventEnabled, double? lat, double? lng)
    {
        if (policy is not null && !policy.GeolocationEnabled) return (null, null);
        return perEventEnabled ? (lat, lng) : (null, null);
    }

    // IP allowlist gate. Only enforced when the employee's policy opts in AND
    // the project actually has an allowlist configured — a project with no
    // allowlist is silently skipped so newly-created projects don't lock
    // everyone out before an admin populates it.
    private async Task<bool> IpAllowedAsync(string employeeId, string? projectId, EmployeePolicy? policy)
    {
        if (policy is null || !policy.RequireIpWhitelist) return true;
        if (string.IsNullOrEmpty(projectId)) return true;

        var project = await _projects.GetByIdAsync(projectId);
        var allowed = (project?.AllowedIps ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (allowed.Length == 0) return true;   // not configured → skip

        var ip = _currentUser.IpAddress;
        if (string.IsNullOrEmpty(ip)) return false;   // enforced but unverifiable → block
        return allowed.Contains(ip, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<int> GetRadiusAsync()
    {
        var orgId = _currentUser.OrganizationId;
        if (string.IsNullOrEmpty(orgId)) return Geo.DefaultRadiusMeters;
        var org = await _organizations.GetByIdAsync(orgId);
        return org?.GeofenceRadiusMeters ?? Geo.DefaultRadiusMeters;
    }

    private static bool OffSiteProofMissing(string? remark, string? photoUrl) =>
        string.IsNullOrWhiteSpace(remark) || string.IsNullOrEmpty(photoUrl);

    // Loads an approval request for a decide operation (single or bulk),
    // shared by every Approve/Reject path so the guards only live in one place.
    //   not found, wrong kind, or caller isn't a current-step approver → Found=false
    //     (collapses "not your approval" into "not found" — pre-existing
    //     behavior from before this change, kept as-is).
    //   already decided → Found=true with an Error.
    private async Task<(AttendanceApprovalRequest? Request, bool Found, string? Error)> LoadDecidableRequestAsync(
        string id,
        string approverId,
        IReadOnlySet<AttendanceApprovalKind> allowedKinds)
    {
        var request = await _approvalRequests.GetByIdAsync(id);
        if (request is null || !allowedKinds.Contains(request.Kind))
            return (null, false, null);

        var approvers = await _router.CurrentApproversAsync(Module, request.EmployeeId, request.CurrentStep);
        if (!approvers.Contains(approverId))
            return (null, false, null);

        if (request.ApprovalStatus != AttendanceApprovalStatus.PENDING)
            return (request, true, "Only pending approvals can be approved or rejected.");

        return (request, true, null);
    }

    private async Task DecideAsync(AttendanceApprovalRequest request, string approverId, bool approve, string? reviewNotes)
    {
        await DecideInMemoryAsync(request, approverId, approve, reviewNotes);
        await _approvalRequests.UpdateAsync(request);

        // The single choke point for EVERY attendance decision — single, break
        // and bulk all route through here — so one publish covers them all.
        var targets = new List<string?> { request.EmployeeId };
        if (request.ApprovalStatus == AttendanceApprovalStatus.PENDING)
        {
            // Still pending means the chain ADVANCED rather than ended: the next
            // step's approver needs it in their queue now.
            targets.AddRange(await _router.CurrentApproversAsync(Module, request.EmployeeId, request.CurrentStep));
        }

        await _realtime.PublishAsync(
            request.OrganizationId,
            targets,
            RealtimeEventDto.For(
                RealtimeScope.ATTENDANCE,
                approve ? RealtimeAction.APPROVED : RealtimeAction.REJECTED,
                request.Id));

        // Persisted in-app notification for the employee, on top of the
        // ephemeral SSE nudge above — same "needs your attention" moment as
        // Claims/Leave's APPROVED/REJECTED.
        await _notifications.NotifyAsync(
            request.OrganizationId, request.EmployeeId, NotificationType.ATTENDANCE_APPROVAL,
            approve ? "Attendance approved" : "Attendance rejected",
            approve
                ? $"Your {KindLabel(request.Kind)} request was approved."
                : $"Your {KindLabel(request.Kind)} request was rejected.{(string.IsNullOrEmpty(request.ReviewNotes) ? "" : $" Reason: {request.ReviewNotes}")}",
            "/attendance");
    }

    private static string KindLabel(AttendanceApprovalKind kind) => kind switch
    {
        AttendanceApprovalKind.CLOCK_IN => "clock-in",
        AttendanceApprovalKind.CLOCK_OUT => "clock-out",
        AttendanceApprovalKind.BREAK_START => "break-start",
        AttendanceApprovalKind.BREAK_END => "break-end",
        _ => "attendance",
    };

    // ---- Import / export ----

    public async Task<TabularExportResult> ExportSummaryAsync(
        DateTime from, DateTime to, string? teamId, TabularFormat format)
    {
        var start = from.Date;
        var end = to.Date;

        var employees = await _employees.GetSnapshotAsync();
        var summary = await _hours.GetOrgHoursSummaryAsync(start, end, teamId);

        // The daily rows behind the summary, same window. Filtered on the local-
        // day key (Date), not TimeIn, so a night shift lands on the day it was
        // booked to rather than the day it happened to end.
        var records = (await _repo.GetAllAsync())
            .Where(r => r.Date.Date >= start && r.Date.Date <= end)
            .OrderBy(r => r.Date)
            .ThenBy(r => employees.NameOf(r.EmployeeId), StringComparer.OrdinalIgnoreCase)
            .ToList();

        var approvals = await _approvalRequests.GetByRecordIdsAsync(records.Select(r => r.Id));
        // "Latest decision wins" — the same rollup GetHistoryAsync shows, so the
        // export and the screen can't disagree.
        var approvalByRecord = approvals
            .GroupBy(a => a.AttendanceRecordId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(a => a.SubmittedAt).First().ApprovalStatus,
                StringComparer.Ordinal);

        var projects = (await _projects.GetAllAsync())
            .ToDictionary(p => p.Id, p => p.Name, StringComparer.Ordinal);

        var caption =
            $"{start:dd MMM yyyy} – {end:dd MMM yyyy}  ·  {summary.Employees.Count} employee(s)  ·  {records.Count} day(s)";

        // PDF gets narrower, printable versions of both tables — A4 landscape
        // can't carry the spreadsheet's full column set legibly.
        var sheets = format == TabularFormat.Pdf
            ? new List<TabularSheet>
            {
                AttendanceSummarySheet.BuildSummary(summary, employees, caption),
                AttendanceSummarySheet.BuildPrintableRecords(
                    records, approvalByRecord, employees, projects, caption),
            }
            : new List<TabularSheet>
            {
                AttendanceSummarySheet.BuildSummary(summary, employees),
                AttendanceSummarySheet.BuildRecords(records, approvalByRecord, employees, projects),
            };

        var fileName = $"attendance-summary-{start:yyyy-MM-dd}-to-{end:yyyy-MM-dd}";
        if (format != TabularFormat.Pdf) return TabularExportResult.From(sheets, format, fileName);

        var organizationId = _currentUser.OrganizationId;
        var organizationName = string.IsNullOrEmpty(organizationId)
            ? "Organization"
            : (await _organizations.GetByIdAsync(organizationId))?.Name ?? "Organization";

        return TabularExportResult.From(
            sheets, format, fileName,
            new TabularPdfHeader(organizationName, "Attendance Report"));
    }

    public TabularExportResult BuildImportTemplate(TabularFormat format) =>
        TabularExportResult.From(
            AttendanceSummarySheet.BuildImportTemplate(), format, "attendance-import-template");

    // Bulk-import historical daily records — a migration off another time-clock,
    // not a second way to clock in. So, deliberately unlike ClockInAsync:
    //
    //   - no geofence, IP-allowlist or off-site-proof checks (they gate what an
    //     employee may do NOW; these days are already in the past),
    //   - no AttendanceApprovalRequest is created, so importing a year of
    //     history doesn't drop a year of approvals into a supervisor's queue,
    //   - a day the employee already has a record for is SKIPPED, never
    //     overwritten — live data always beats imported data.
    public async Task<TabularImportResult> ImportAsync(byte[] content, TabularFormat format)
    {
        IReadOnlyList<IReadOnlyList<string>> rows;
        try
        {
            rows = TabularReader.Read(content, format);
        }
        catch (InvalidDataException ex)
        {
            return TabularImportResult.FileError(ex.Message);
        }

        if (rows.Count == 0) return TabularImportResult.FileError("The file is empty.");

        var columns = AttendanceSummarySheet.ImportColumns;
        var (map, missing) = TabularHeaderMap.Build(
            rows[0], columns, EmployeeImportColumns.IdentityGroup);
        if (map is null)
            return TabularImportResult.FileError($"Missing required column(s): {string.Join(", ", missing)}.");
        if (rows.Count == 1)
            return TabularImportResult.FileError("The file has a header row but no data rows.");

        var result = new TabularImportResult();
        var employees = await _employees.GetSnapshotAsync();
        var projectIdsByName = (await _projects.GetAllAsync())
            .GroupBy(p => p.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        // One read of the existing rows, then dedupe in memory: a per-row
        // GetForEmployeeOnDateAsync would be one query per line of the file.
        var taken = (await _repo.GetAllAsync())
            .Select(r => DayKey(r.EmployeeId, r.Date))
            .ToHashSet(StringComparer.Ordinal);

        var now = DateTime.UtcNow;
        var imported = 0;

        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowNumber = i + 1;

            if (TabularTemplate.IsExampleRow(map, row, columns))
            {
                result.CountSkipped();
                continue;
            }

            var email = map.Cell(row, "employeeEmail");
            var name = map.Cell(row, "employeeName");
            var (employeeId, ambiguous) = employees.Resolve(email, name);
            if (ambiguous)
            {
                result.Fail(rowNumber, $"More than one employee is named '{name}'. Use the Employee Email column.");
                continue;
            }
            if (employeeId is null)
            {
                result.Fail(rowNumber, $"No employee in this organization matches '{(email.Length > 0 ? email : name)}'.");
                continue;
            }

            var date = TabularCell.Date(map.Cell(row, "date"));
            if (date is null)
            {
                result.Fail(rowNumber, "Date must be a date, e.g. 2026-01-15.");
                continue;
            }

            var key = DayKey(employeeId, date.Value);
            if (taken.Contains(key))
            {
                result.CountSkipped();
                continue;
            }

            // Both accept a bare time ("09:03") resolved against Date, or a full
            // timestamp — whichever the source system exported.
            var timeIn = TabularCell.Instant(map.Cell(row, "clockIn"), date);
            var timeOut = TabularCell.Instant(map.Cell(row, "clockOut"), date);

            if (!TabularCell.IsBlank(map.Cell(row, "clockIn")) && timeIn is null)
            {
                result.Fail(rowNumber, "Clock In must be a time (09:03) or a timestamp (2026-01-15 09:03).");
                continue;
            }
            if (!TabularCell.IsBlank(map.Cell(row, "clockOut")) && timeOut is null)
            {
                result.Fail(rowNumber, "Clock Out must be a time (18:12) or a timestamp (2026-01-15 18:12).");
                continue;
            }

            // A shift that runs past midnight exports as out < in. Rolling the
            // end forward a day is the only reading that yields a sane duration.
            if (timeIn is not null && timeOut is not null && timeOut < timeIn)
                timeOut = timeOut.Value.AddDays(1);

            var statusCell = map.Cell(row, "status");
            var status = TabularCell.IsBlank(statusCell)
                ? DeriveStatus(timeIn, timeOut)
                : TabularCell.Enum<AttendanceStatus>(statusCell);
            if (status is null)
            {
                result.Fail(rowNumber,
                    $"Status must be one of: {string.Join(", ", Enum.GetNames<AttendanceStatus>())}.");
                continue;
            }

            string? projectId = null;
            var projectName = map.Cell(row, "project");
            if (!TabularCell.IsBlank(projectName) &&
                projectIdsByName.TryGetValue(projectName.Trim(), out var foundProject))
                projectId = foundProject;

            await _repo.AddAsync(new AttendanceRecord
            {
                EmployeeId = employeeId,
                Date = AttendanceTime.StartOfLocalDay(date.Value),
                TimeIn = timeIn,
                TimeOut = timeOut,
                DurationMin = timeIn is not null && timeOut is not null
                    ? (int)Math.Round((timeOut.Value - timeIn.Value).TotalMinutes)
                    : null,
                Status = status.Value,
                ProjectId = projectId,
                Location = TabularCell.Text(map.Cell(row, "location"), 200),
                Remark = TabularCell.Text(map.Cell(row, "remark")),
                CreatedAt = now,
                UpdatedAt = now,
            });

            taken.Add(key);
            result.CountImported();
            imported++;
        }

        if (imported > 0) await NotifyImportAsync(employees);
        return result;
    }

    private static AttendanceStatus DeriveStatus(DateTime? timeIn, DateTime? timeOut)
    {
        if (timeIn is null) return AttendanceStatus.MISSING;
        return timeOut is null ? AttendanceStatus.CLOCKED_IN : AttendanceStatus.CLOCKED_OUT;
    }

    private static string DayKey(string employeeId, DateTime date) =>
        $"{employeeId}|{date:yyyy-MM-dd}";

    // One nudge for the whole import rather than one per row — see the same
    // reasoning in ClaimsService.
    private async Task NotifyImportAsync(EmployeeRowIndex employees)
    {
        var organizationId = _currentUser.OrganizationId;
        if (string.IsNullOrEmpty(organizationId)) return;

        await _realtime.PublishAsync(
            organizationId,
            employees.Members.Select(m => (string?)m.Id),
            RealtimeEventDto.For(RealtimeScope.ATTENDANCE, RealtimeAction.UPDATED));
    }

    // A newly-submitted request: nudge whoever has to review it via realtime
    // only. The persisted per-submission notification was removed in favor of
    // the daily cross-module digest (see Modules/Approvals/ApprovalDigestService)
    // — an approver's open tab still live-updates via the SSE nudge below.
    private async Task NotifyPendingAsync(AttendanceApprovalRequest request, RealtimeAction action)
    {
        var approvers = await _router.CurrentApproversAsync(Module, request.EmployeeId, request.CurrentStep);
        await _realtime.PublishAsync(
            request.OrganizationId,
            approvers,
            RealtimeEventDto.For(RealtimeScope.ATTENDANCE, action, request.Id));
    }

    private async Task DecideInMemoryAsync(AttendanceApprovalRequest request, string approverId, bool approve, string? reviewNotes)
    {
        var now = DateTime.UtcNow;
        request.ReviewerId = approverId;

        if (!approve)
        {
            request.ApprovalStatus = AttendanceApprovalStatus.REJECTED;
            request.ReviewNotes = reviewNotes;
            request.DecidedAt = now;
            request.UpdatedAt = now;
            return;
        }

        var stepCount = await _router.StepCountAsync(Module, request.EmployeeId);
        var isFinal = request.CurrentStep + 1 >= stepCount;
        if (isFinal)
        {
            request.ApprovalStatus = AttendanceApprovalStatus.APPROVED;
            request.DecidedAt = now;
            // A time-adjustment request only actually changes the record once
            // it's fully approved — rejecting it just leaves the record as-is.
            if (request.OriginalEventAt is not null)
                await ApplyAdjustmentAsync(request);
        }
        else
        {
            request.CurrentStep += 1;
        }

        request.UpdatedAt = now;
    }

    // Every attendance request is filed through here.
    //
    // A request is created PENDING and waits for an approver — unless the
    // employee HAS no approver. Someone at the top of the hierarchy (admins are
    // not in it, see OrgRoles) has zero approval steps, and a PENDING request
    // with no one to route to is invisible in every queue and rejected for
    // every caller: it sits unresolved forever. There is nobody left to ask, so
    // submitting IS the decision.
    // Resolves requests that no longer have anyone to approve them.
    //
    // The submit-time rule only applies going forward. Rows written earlier can
    // become unreachable when the hierarchy changes underneath them — most
    // sharply when admins were removed from it (see OrgRoles), which deleted the
    // step that requests parked on. An unreachable request appears in no queue
    // and is refused for every caller: it is stuck, not pending.
    //
    // `apply: false` counts them without changing anything, so the damage can be
    // inspected before it is acted on. Idempotent: a resolved row is no longer
    // PENDING, so a second run finds nothing.
    public async Task<int> ReconcileUnreachableApprovalsAsync(bool apply)
    {
        var now = DateTime.UtcNow;
        var pending = await _approvalRequests.GetOpenByKindsAsync(AllKinds);
        var stuck = 0;

        foreach (var request in pending)
        {
            var approvers = await _router.CurrentApproversAsync(Module, request.EmployeeId, request.CurrentStep);
            if (approvers.Count > 0) continue;

            stuck++;
            if (!apply) continue;

            request.ApprovalStatus = AttendanceApprovalStatus.APPROVED;
            request.DecidedAt = now;
            request.UpdatedAt = now;
            // ReviewerId stays null — nobody reviewed it. See FileRequestAsync.
            await _approvalRequests.UpdateAsync(request);

            if (request.OriginalEventAt is not null)
                await ApplyAdjustmentAsync(request);
        }

        return stuck;
    }

    private async Task<AttendanceApprovalRequest> FileRequestAsync(AttendanceApprovalRequest request)
    {
        var stepCount = await _router.StepCountAsync(Module, request.EmployeeId);
        if (stepCount == 0)
        {
            request.ApprovalStatus = AttendanceApprovalStatus.APPROVED;
            request.DecidedAt = request.SubmittedAt;
            // No ReviewerId: nobody reviewed it. Leaving it null keeps the audit
            // honest — "approved by default", not "approved by <the applicant>".
        }

        var created = await _approvalRequests.AddAsync(request);

        // A time adjustment only changes the record once fully approved, and
        // this one just was.
        if (created.ApprovalStatus == AttendanceApprovalStatus.APPROVED && created.OriginalEventAt is not null)
            await ApplyAdjustmentAsync(created);

        return created;
    }

    private async Task ApplyAdjustmentAsync(AttendanceApprovalRequest request)
    {
        var record = await _repo.GetByIdAsync(request.AttendanceRecordId);
        if (record is null) return;

        if (request.Kind == AttendanceApprovalKind.CLOCK_IN) record.TimeIn = request.EventAt;
        else if (request.Kind == AttendanceApprovalKind.CLOCK_OUT) record.TimeOut = request.EventAt;

        if (record.TimeIn is not null && record.TimeOut is not null)
            record.DurationMin = (int)Math.Round((record.TimeOut.Value - record.TimeIn.Value).TotalMinutes);

        record.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(record);
    }

    private async Task<AttendanceRecordDto?> ToRecordDtoAsync(AttendanceApprovalRequest? request)
    {
        if (request is null) return null;
        var record = await _repo.GetByIdAsync(request.AttendanceRecordId);
        if (record is null) return null;
        var approvals = await _approvalRequests.GetByRecordIdsAsync([record.Id]);
        return ToDto(record, approvals);
    }

    private async Task<AttendanceBreakDto?> ToBreakDtoAsync(AttendanceApprovalRequest? request)
    {
        if (request?.AttendanceBreakId is null) return null;
        var brk = await _breaks.GetByIdAsync(request.AttendanceBreakId);
        if (brk is null) return null;
        var approvals = await _approvalRequests.GetByBreakIdsAsync([brk.Id]);
        return ToBreakDto(brk, approvals);
    }

    private static AttendanceBulkResult BuildBulkResult(List<AttendanceBulkResultItem> items)
    {
        var succeeded = items.Count(i => i.Ok);
        return new AttendanceBulkResult(succeeded, items.Count - succeeded, items);
    }

    // The local "HH:mm" this employee was due to start: their effective shift
    // (assigned, else the project's, else the org default) and finally the org's
    // working hours. Null when nothing is configured, which AttendanceLateness
    // reads as "no opinion" rather than "on time".
    private async Task<string?> ScheduledStartAsync(string employeeId)
    {
        var shift = await _shifts.GetEffectiveShiftAsync(employeeId);
        if (!string.IsNullOrWhiteSpace(shift?.StartTime)) return shift!.StartTime;

        var orgId = _currentUser.OrganizationId;
        if (string.IsNullOrEmpty(orgId)) return null;
        return (await _organizations.GetByIdAsync(orgId))?.WorkingHoursStart;
    }

    private static AttendanceActionResult OffSiteRequired(double? distance) => new(
        false,
        null,
        "You're outside the project geofence. Add a remark and a photo to clock in from here.",
        OffSiteCode,
        distance);

    // Unlike the off-site case, there's no remark/photo override — the IP
    // allowlist is a hard block, so the client shouldn't offer a retry path.
    private static AttendanceActionResult IpNotAllowed() => new(
        false,
        null,
        "You're not on an approved network for this project. Connect to the site network and try again.",
        IpNotAllowedCode);

    // Stored instants are UTC; MySQL drops the Kind, so re-stamp it before
    // formatting so the JSON carries the trailing "Z".
    private static string? Iso(DateTime? d) =>
        d is null ? null : DateTime.SpecifyKind(d.Value, DateTimeKind.Utc).ToString("o");

    private static string? Clean(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static AttendanceSessionDto ToSessionDto(AttendanceSession s) => new()
    {
        Id = s.Id,
        StartedAt = Iso(s.StartedAt) ?? string.Empty,
        EndedAt = Iso(s.EndedAt),
        DurationMin = s.DurationMin,
        LateByMin = s.LateByMin,
        Status = s.Status,
        ClockInDistanceMeters = s.ClockInDistanceMeters,
        ClockOutDistanceMeters = s.ClockOutDistanceMeters,
        ClockInPhotoUrl = s.ClockInPhotoUrl,
        ClockOutPhotoUrl = s.ClockOutPhotoUrl,
    };

    private static AttendanceRecordDto ToDto(
        AttendanceRecord r,
        IReadOnlyList<AttendanceApprovalRequest> approvals,
        IReadOnlyList<AttendanceSession>? sessions = null)
    {
        var latest = approvals.OrderByDescending(a => a.SubmittedAt).FirstOrDefault();
        return new AttendanceRecordDto
        {
            Id = r.Id,
            EmployeeId = r.EmployeeId,
            Date = r.Date.ToString("yyyy-MM-dd"),
            TimeIn = Iso(r.TimeIn),
            TimeOut = Iso(r.TimeOut),
            DurationMin = r.DurationMin,
            LateByMin = r.LateByMin,
            Location = r.Location,
            ProjectId = r.ProjectId,
            ClockInLat = r.ClockInLat,
            ClockInLng = r.ClockInLng,
            ClockInDistanceMeters = r.ClockInDistanceMeters,
            ClockOutLat = r.ClockOutLat,
            ClockOutLng = r.ClockOutLng,
            ClockOutDistanceMeters = r.ClockOutDistanceMeters,
            ClockInPhotoUrl = r.ClockInPhotoUrl,
            ClockOutPhotoUrl = r.ClockOutPhotoUrl,
            Status = r.Status,
            ApprovalStatus = latest?.ApprovalStatus ?? AttendanceApprovalStatus.PENDING,
            CurrentStep = latest?.CurrentStep ?? 0,
            ReviewNotes = latest?.ReviewNotes,
            SubmittedAt = Iso(latest?.SubmittedAt),
            DecidedAt = Iso(latest?.DecidedAt),
            Approvals = approvals.Select(a => ToApprovalRequestDto(a, null)).ToList(),
            Sessions = (sessions ?? [])
                .OrderBy(x => x.StartedAt)
                .Select(ToSessionDto)
                .ToList(),
            Notes = r.Notes,
            Remark = r.Remark,
            CreatedAt = Iso(r.CreatedAt) ?? string.Empty,
            UpdatedAt = Iso(r.UpdatedAt) ?? string.Empty,
        };
    }

    private static AttendanceBreakDto ToBreakDto(AttendanceBreak b, IReadOnlyList<AttendanceApprovalRequest> approvals)
    {
        var latest = approvals.OrderByDescending(a => a.SubmittedAt).FirstOrDefault();
        return new AttendanceBreakDto
        {
            Id = b.Id,
            AttendanceSessionId = b.AttendanceSessionId,
            AttendanceRecordId = b.AttendanceRecordId,
            StartedAt = Iso(b.StartedAt) ?? string.Empty,
            EndedAt = Iso(b.EndedAt),
            DurationMin = b.EndedAt is null ? null : (int)Math.Round((b.EndedAt.Value - b.StartedAt).TotalMinutes),
            StartLat = b.StartLat,
            StartLng = b.StartLng,
            EndLat = b.EndLat,
            EndLng = b.EndLng,
            Remark = b.Remark,
            ApprovalStatus = latest?.ApprovalStatus ?? AttendanceApprovalStatus.PENDING,
            CurrentStep = latest?.CurrentStep ?? 0,
            ReviewNotes = latest?.ReviewNotes,
            SubmittedAt = Iso(latest?.SubmittedAt),
            DecidedAt = Iso(latest?.DecidedAt),
            Approvals = approvals.Select(a => ToApprovalRequestDto(a, null)).ToList(),
        };
    }

    private static AttendanceApprovalRequestDto ToApprovalRequestDto(AttendanceApprovalRequest a, string? employeeEmail) => new()
    {
        Id = a.Id,
        EmployeeId = a.EmployeeId,
        EmployeeEmail = employeeEmail,
        Kind = a.Kind,
        EventAt = Iso(a.EventAt) ?? string.Empty,
        OriginalEventAt = Iso(a.OriginalEventAt),
        Reason = a.Reason,
        ApprovalStatus = a.ApprovalStatus,
        CurrentStep = a.CurrentStep,
        ReviewNotes = a.ReviewNotes,
        ReviewerId = a.ReviewerId,
        SubmittedAt = Iso(a.SubmittedAt),
        DecidedAt = Iso(a.DecidedAt),
        AttendanceRecordId = a.AttendanceRecordId,
        AttendanceSessionId = a.AttendanceSessionId,
        AttendanceBreakId = a.AttendanceBreakId,
    };
}
