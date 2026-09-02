using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Attendance.Dtos;
using AltomateHR.Api.Modules.Attendance.Entities;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Policies;
using AltomateHR.Api.Modules.Policies.Entities;
using AltomateHR.Api.Modules.Projects;
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
    private readonly ICurrentUser _currentUser;
    private readonly IAttendancePhotoStorage _photos;
    private readonly IPolicyService _policies;
    private readonly ISupervisionService _supervision;
    private readonly IApprovalRouter _router;

    public AttendanceService(
        IAttendanceRepository repo,
        IAttendanceSessionRepository sessions,
        IAttendanceBreakRepository breaks,
        IAttendanceApprovalRequestRepository approvalRequests,
        IProjectService projects,
        IOrganizationService organizations,
        ICurrentUser currentUser,
        IAttendancePhotoStorage photos,
        IPolicyService policies,
        ISupervisionService supervision,
        IApprovalRouter router,
        IDirectoryService directory)
    {
        _repo = repo;
        _sessions = sessions;
        _breaks = breaks;
        _approvalRequests = approvalRequests;
        _projects = projects;
        _organizations = organizations;
        _currentUser = currentUser;
        _photos = photos;
        _policies = policies;
        _supervision = supervision;
        _router = router;
        _directory = directory;
    }

    public async Task<AttendanceRecordDto?> GetTodayAsync(string employeeId)
    {
        var today = AttendanceTime.StartOfLocalDay(DateTime.UtcNow);
        var record = await _repo.GetForEmployeeOnDateAsync(employeeId, today);
        if (record is null) return null;
        var approvals = await _approvalRequests.GetByRecordIdsAsync([record.Id]);
        return ToDto(record, approvals);
    }

    public async Task<IEnumerable<AttendanceRecordDto>> GetHistoryAsync(string userId, bool isAdmin)
    {
        var records = isAdmin
            ? await _repo.GetAllAsync()
            : await _repo.GetByEmployeeAsync(userId);
        var approvals = await _approvalRequests.GetByRecordIdsAsync(records.Select(r => r.Id));
        var byRecord = approvals.GroupBy(a => a.AttendanceRecordId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<AttendanceApprovalRequest>)g.ToList());
        return records.Select(r => ToDto(r, byRecord.GetValueOrDefault(r.Id, [])));
    }

    public async Task<IEnumerable<AttendanceApprovalRequestDto>> GetTeamApprovalsAsync(string userId)
    {
        var pending = await _approvalRequests.GetOpenByKindsAsync(RecordKinds);
        var visible = new List<AttendanceApprovalRequest>();
        foreach (var request in pending)
        {
            var approvers = await _router.CurrentApproversAsync(Module, request.EmployeeId, request.CurrentStep);
            if (approvers.Contains(userId)) visible.Add(request);
        }

        var emails = await _supervision.GetEmailsAsync(visible.Select(r => r.EmployeeId).Distinct());
        return visible.Select(r => ToApprovalRequestDto(r, emails.GetValueOrDefault(r.EmployeeId)));
    }

    public async Task<AttendanceActionResult> ClockInAsync(string employeeId, ClockInDto dto)
    {
        var now = DateTime.UtcNow;
        var today = AttendanceTime.StartOfLocalDay(now);
        var existing = await _repo.GetForEmployeeOnDateAsync(employeeId, today);

        if (existing is not null && existing.TimeIn is not null)
        {
            var currentApprovals = await _approvalRequests.GetByRecordIdsAsync([existing.Id]);
            return new AttendanceActionResult(false, ToDto(existing, currentApprovals),
                existing.TimeOut is null
                    ? "You're already clocked in today."
                    : "You've already completed your attendance for today.");
        }

        var effectiveProjectId = dto.ProjectId ?? existing?.ProjectId;
        var policy = await _policies.GetEffectivePolicyAsync(employeeId);

        if (!await IpAllowedAsync(employeeId, effectiveProjectId, policy))
            return IpNotAllowed();

        var (_, distance, offSite) = await EvaluateGeofenceAsync(employeeId, effectiveProjectId, dto.Lat, dto.Lng);
        if (offSite && OffSiteProofMissing(dto.Remark, dto.PhotoUrl))
            return OffSiteRequired(distance);

        var (capturedLat, capturedLng) =
            CaptureCoords(policy, policy?.CaptureLocationOnClockIn ?? true, dto.Lat, dto.Lng);

        AttendanceRecord record;
        if (existing is null)
        {
            record = new AttendanceRecord
            {
                EmployeeId = employeeId,
                Date = today,
                TimeIn = now,
                Status = AttendanceStatus.CLOCKED_IN,
                ProjectId = effectiveProjectId,
                Location = dto.Location,
                Remark = dto.Remark,
                ClockInLat = capturedLat,
                ClockInLng = capturedLng,
                ClockInDistanceMeters = distance,
                ClockInPhotoUrl = dto.PhotoUrl,
                CreatedAt = now,
                UpdatedAt = now,
            };
            record = await _repo.AddAsync(record);
        }
        else
        {
            // A row already exists for today (e.g. a pre-seeded MISSING/ON_LEAVE day)
            // but no clock-in yet — fill it in rather than violating the unique key.
            existing.TimeIn = now;
            existing.Status = AttendanceStatus.CLOCKED_IN;
            existing.ProjectId = effectiveProjectId;
            existing.Location = dto.Location ?? existing.Location;
            existing.Remark = dto.Remark ?? existing.Remark;
            existing.ClockInLat = capturedLat;
            existing.ClockInLng = capturedLng;
            existing.ClockInDistanceMeters = distance;
            existing.ClockInPhotoUrl = dto.PhotoUrl;
            existing.UpdatedAt = now;
            await _repo.UpdateAsync(existing);
            record = existing;
        }

        var session = await _sessions.AddAsync(new AttendanceSession
        {
            AttendanceRecordId = record.Id,
            EmployeeId = employeeId,
            StartedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        });

        var request = await _approvalRequests.AddAsync(new AttendanceApprovalRequest
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

        return new AttendanceActionResult(true, ToDto(record, [request]));
    }

    public async Task<AttendanceActionResult> ClockOutAsync(string employeeId, ClockOutDto dto)
    {
        var now = DateTime.UtcNow;
        var today = AttendanceTime.StartOfLocalDay(now);
        var record = await _repo.GetForEmployeeOnDateAsync(employeeId, today);

        if (record is null || record.TimeIn is null)
            return new AttendanceActionResult(false, null, "You haven't clocked in today.");

        if (record.TimeOut is not null)
        {
            var currentApprovals = await _approvalRequests.GetByRecordIdsAsync([record.Id]);
            return new AttendanceActionResult(false, ToDto(record, currentApprovals), "You've already clocked out today.");
        }

        var policy = await _policies.GetEffectivePolicyAsync(employeeId);

        if (!await IpAllowedAsync(employeeId, record.ProjectId, policy))
            return IpNotAllowed();

        var (_, distance, offSite) = await EvaluateGeofenceAsync(employeeId, record.ProjectId, dto.Lat, dto.Lng);
        if (offSite && OffSiteProofMissing(dto.Remark, dto.PhotoUrl))
            return OffSiteRequired(distance);

        var (capturedLat, capturedLng) =
            CaptureCoords(policy, policy?.CaptureLocationOnClockOut ?? true, dto.Lat, dto.Lng);

        record.TimeOut = now;
        record.DurationMin = (int)Math.Round((now - record.TimeIn.Value).TotalMinutes);
        record.Status = AttendanceStatus.CLOCKED_OUT;
        record.ClockOutLat = capturedLat;
        record.ClockOutLng = capturedLng;
        record.ClockOutDistanceMeters = distance;
        record.ClockOutPhotoUrl = dto.PhotoUrl;
        if (!string.IsNullOrWhiteSpace(dto.Remark)) record.Remark = dto.Remark;
        record.UpdatedAt = now;
        await _repo.UpdateAsync(record);

        var session = await _sessions.GetOpenForRecordAsync(record.Id);
        if (session is not null)
        {
            session.EndedAt = now;
            session.UpdatedAt = now;
            await _sessions.UpdateAsync(session);
        }

        await _approvalRequests.AddAsync(new AttendanceApprovalRequest
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

        var allApprovals = await _approvalRequests.GetByRecordIdsAsync([record.Id]);
        return new AttendanceActionResult(true, ToDto(record, allApprovals));
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

        var request = await _approvalRequests.AddAsync(new AttendanceApprovalRequest
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

        await _approvalRequests.AddAsync(new AttendanceApprovalRequest
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
        var created = await _approvalRequests.AddAsync(new AttendanceApprovalRequest
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
                await _approvalRequests.AddAsync(new AttendanceApprovalRequest
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

        var countByReviewer = new Dictionary<string, int>();
        foreach (var request in pending)
        {
            var approvers = await _router.CurrentApproversAsync(Module, request.EmployeeId, request.CurrentStep);
            foreach (var reviewerId in approvers)
                countByReviewer[reviewerId] = countByReviewer.GetValueOrDefault(reviewerId) + 1;
        }

        return countByReviewer
            .Select(kv => new OrgApprovalDigestEntryDto { ReviewerId = kv.Key, PendingCount = kv.Value })
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

    private static AttendanceRecordDto ToDto(AttendanceRecord r, IReadOnlyList<AttendanceApprovalRequest> approvals)
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
