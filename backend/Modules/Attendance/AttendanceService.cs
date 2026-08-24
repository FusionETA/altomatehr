using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Attendance.Dtos;
using AltomateHR.Api.Modules.Attendance.Entities;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Policies;
using AltomateHR.Api.Modules.Projects;
using AltomateHR.Api.Modules.Teams;

namespace AltomateHR.Api.Modules.Attendance;

// Business logic: clock-in / clock-out + reads. One record per employee per
// local business day. Geofence enforcement: clocking against a project that has
// a geofence centre, from outside the org radius (or with no GPS at all),
// requires BOTH a remark and a photo — matching the current AltomateHR.
public class AttendanceService : IAttendanceService
{
    private const ApprovalModule Module = ApprovalModule.ATTENDANCE;
    private const string OffSiteCode = "OFF_SITE_ACTION_REQUIRED";

    private readonly IAttendanceRepository _repo;
    private readonly IAttendanceSessionRepository _sessions;
    private readonly IAttendanceBreakRepository _breaks;
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
        IProjectService projects,
        IOrganizationService organizations,
        ICurrentUser currentUser,
        IAttendancePhotoStorage photos,
        IPolicyService policies,
        ISupervisionService supervision,
        IApprovalRouter router)
    {
        _repo = repo;
        _sessions = sessions;
        _breaks = breaks;
        _projects = projects;
        _organizations = organizations;
        _currentUser = currentUser;
        _photos = photos;
        _policies = policies;
        _supervision = supervision;
        _router = router;
    }

    public async Task<AttendanceRecordDto?> GetTodayAsync(string employeeId)
    {
        var today = AttendanceTime.StartOfLocalDay(DateTime.UtcNow);
        var record = await _repo.GetForEmployeeOnDateAsync(employeeId, today);
        return record is null ? null : ToDto(record);
    }

    public async Task<IEnumerable<AttendanceRecordDto>> GetHistoryAsync(string userId, bool isAdmin)
    {
        var records = isAdmin
            ? await _repo.GetAllAsync()
            : await _repo.GetByEmployeeAsync(userId);
        return records.Select(ToDto);
    }

    public async Task<IEnumerable<AttendanceRecordDto>> GetTeamApprovalsAsync(string userId)
    {
        var all = await _repo.GetAllAsync();
        var visible = new List<AttendanceRecord>();
        foreach (var record in all.Where(r => r.ApprovalStatus == AttendanceApprovalStatus.PENDING))
        {
            var approvers = await _router.CurrentApproversAsync(Module, record.EmployeeId, record.CurrentStep);
            if (approvers.Contains(userId)) visible.Add(record);
        }

        var emails = await _supervision.GetEmailsAsync(visible.Select(r => r.EmployeeId).Distinct());
        return visible.Select(record =>
        {
            var dto = ToDto(record);
            dto.EmployeeEmail = emails.GetValueOrDefault(record.EmployeeId);
            return dto;
        });
    }

    public async Task<AttendanceActionResult> ClockInAsync(string employeeId, ClockInDto dto)
    {
        var now = DateTime.UtcNow;
        var today = AttendanceTime.StartOfLocalDay(now);
        var existing = await _repo.GetForEmployeeOnDateAsync(employeeId, today);

        if (existing is not null && existing.TimeIn is not null)
        {
            return new AttendanceActionResult(false, ToDto(existing),
                existing.TimeOut is null
                    ? "You're already clocked in today."
                    : "You've already completed your attendance for today.");
        }

        var effectiveProjectId = dto.ProjectId ?? existing?.ProjectId;
        var (_, distance, offSite) = await EvaluateGeofenceAsync(employeeId, effectiveProjectId, dto.Lat, dto.Lng);
        if (offSite && OffSiteProofMissing(dto.Remark, dto.PhotoUrl))
            return OffSiteRequired(distance);

        if (existing is null)
        {
            var record = new AttendanceRecord
            {
                EmployeeId = employeeId,
                Date = today,
                TimeIn = now,
                Status = AttendanceStatus.CLOCKED_IN,
                ApprovalStatus = AttendanceApprovalStatus.PENDING,
                CurrentStep = 0,
                ProjectId = effectiveProjectId,
                Location = dto.Location,
                Remark = dto.Remark,
                ClockInLat = dto.Lat,
                ClockInLng = dto.Lng,
                ClockInDistanceMeters = distance,
                ClockInPhotoUrl = dto.PhotoUrl,
                SubmittedAt = now,
                DecidedAt = null,
                ReviewNotes = null,
                CreatedAt = now,
                UpdatedAt = now,
            };
            var saved = await _repo.AddAsync(record);
            await _sessions.AddAsync(new AttendanceSession
            {
                AttendanceRecordId = saved.Id,
                EmployeeId = employeeId,
                StartedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
            });
            return new AttendanceActionResult(true, ToDto(saved));
        }

        // A row already exists for today (e.g. a pre-seeded MISSING/ON_LEAVE day)
        // but no clock-in yet — fill it in rather than violating the unique key.
        existing.TimeIn = now;
        existing.Status = AttendanceStatus.CLOCKED_IN;
        existing.ApprovalStatus = AttendanceApprovalStatus.PENDING;
        existing.CurrentStep = 0;
        existing.ProjectId = effectiveProjectId;
        existing.Location = dto.Location ?? existing.Location;
        existing.Remark = dto.Remark ?? existing.Remark;
        existing.ClockInLat = dto.Lat;
        existing.ClockInLng = dto.Lng;
        existing.ClockInDistanceMeters = distance;
        existing.ClockInPhotoUrl = dto.PhotoUrl;
        existing.SubmittedAt = now;
        existing.DecidedAt = null;
        existing.ReviewNotes = null;
        existing.UpdatedAt = now;
        await _repo.UpdateAsync(existing);
        await _sessions.AddAsync(new AttendanceSession
        {
            AttendanceRecordId = existing.Id,
            EmployeeId = employeeId,
            StartedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        });
        return new AttendanceActionResult(true, ToDto(existing));
    }

    public async Task<AttendanceActionResult> ClockOutAsync(string employeeId, ClockOutDto dto)
    {
        var now = DateTime.UtcNow;
        var today = AttendanceTime.StartOfLocalDay(now);
        var record = await _repo.GetForEmployeeOnDateAsync(employeeId, today);

        if (record is null || record.TimeIn is null)
            return new AttendanceActionResult(false, null, "You haven't clocked in today.");

        if (record.TimeOut is not null)
            return new AttendanceActionResult(false, ToDto(record), "You've already clocked out today.");

        var (_, distance, offSite) = await EvaluateGeofenceAsync(employeeId, record.ProjectId, dto.Lat, dto.Lng);
        if (offSite && OffSiteProofMissing(dto.Remark, dto.PhotoUrl))
            return OffSiteRequired(distance);

        record.TimeOut = now;
        record.DurationMin = (int)Math.Round((now - record.TimeIn.Value).TotalMinutes);
        record.Status = AttendanceStatus.CLOCKED_OUT;
        record.ApprovalStatus = AttendanceApprovalStatus.PENDING;
        record.CurrentStep = 0;
        record.ClockOutLat = dto.Lat;
        record.ClockOutLng = dto.Lng;
        record.ClockOutDistanceMeters = distance;
        record.ClockOutPhotoUrl = dto.PhotoUrl;
        if (!string.IsNullOrWhiteSpace(dto.Remark)) record.Remark = dto.Remark;
        record.SubmittedAt = now;
        record.DecidedAt = null;
        record.ReviewNotes = null;
        record.UpdatedAt = now;
        await _repo.UpdateAsync(record);

        var session = await _sessions.GetOpenForRecordAsync(record.Id);
        if (session is not null)
        {
            session.EndedAt = now;
            session.UpdatedAt = now;
            await _sessions.UpdateAsync(session);
        }

        return new AttendanceActionResult(true, ToDto(record));
    }

    public async Task<AttendanceTransitionResult> ApproveAsync(string id, string approverId)
    {
        var (record, error) = await AuthorizeAsync(id, approverId);
        if (error is not null) return error;

        var now = DateTime.UtcNow;
        var stepCount = await _router.StepCountAsync(Module, record!.EmployeeId);
        var isFinal = record.CurrentStep + 1 >= stepCount;
        if (isFinal)
        {
            record.ApprovalStatus = AttendanceApprovalStatus.APPROVED;
            record.DecidedAt = now;
        }
        else
        {
            record.CurrentStep += 1;
        }

        record.UpdatedAt = now;
        await _repo.UpdateAsync(record);
        return new AttendanceTransitionResult(true, true, ToDto(record));
    }

    public async Task<AttendanceTransitionResult> RejectAsync(string id, string approverId, string? reviewNotes)
    {
        var (record, error) = await AuthorizeAsync(id, approverId);
        if (error is not null) return error;

        var cleanedReviewNotes = Clean(reviewNotes);
        if (cleanedReviewNotes is null)
            return new AttendanceTransitionResult(true, false, null,
                "Enter a rejection remark before rejecting this attendance record.");

        var now = DateTime.UtcNow;
        record!.ApprovalStatus = AttendanceApprovalStatus.REJECTED;
        record.ReviewNotes = cleanedReviewNotes;
        record.DecidedAt = now;
        record.UpdatedAt = now;
        await _repo.UpdateAsync(record);
        return new AttendanceTransitionResult(true, true, ToDto(record));
    }

    public async Task<AttendanceBreakActionResult> StartBreakAsync(string employeeId, StartBreakDto dto)
    {
        var now = DateTime.UtcNow;
        var today = AttendanceTime.StartOfLocalDay(now);
        var record = await _repo.GetForEmployeeOnDateAsync(employeeId, today);
        if (record is null || record.TimeIn is null || record.TimeOut is not null)
            return new AttendanceBreakActionResult(false, null, "Clock in before starting a break.");

        var session = await _sessions.GetOpenForRecordAsync(record.Id);
        if (session is null)
            return new AttendanceBreakActionResult(false, null, "Clock in before starting a break.");

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
            ApprovalStatus = AttendanceApprovalStatus.PENDING,
            CurrentStep = 0,
            SubmittedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var saved = await _breaks.AddAsync(brk);
        return new AttendanceBreakActionResult(true, ToBreakDto(saved));
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
        // The end event needs its own approval pass, mirroring how ClockOutAsync
        // resets AttendanceRecord's approval fields on top of clock-in's decision.
        brk.ApprovalStatus = AttendanceApprovalStatus.PENDING;
        brk.CurrentStep = 0;
        brk.SubmittedAt = now;
        brk.DecidedAt = null;
        brk.ReviewNotes = null;
        brk.UpdatedAt = now;
        await _breaks.UpdateAsync(brk);
        return new AttendanceBreakActionResult(true, ToBreakDto(brk));
    }

    public async Task<AttendanceBreakTransitionResult> ApproveBreakAsync(string id, string approverId)
    {
        var (brk, error) = await AuthorizeBreakAsync(id, approverId);
        if (error is not null) return error;

        var now = DateTime.UtcNow;
        var stepCount = await _router.StepCountAsync(Module, brk!.EmployeeId);
        var isFinal = brk.CurrentStep + 1 >= stepCount;
        if (isFinal)
        {
            brk.ApprovalStatus = AttendanceApprovalStatus.APPROVED;
            brk.DecidedAt = now;
        }
        else
        {
            brk.CurrentStep += 1;
        }

        brk.UpdatedAt = now;
        await _breaks.UpdateAsync(brk);
        return new AttendanceBreakTransitionResult(true, true, ToBreakDto(brk));
    }

    public async Task<AttendanceBreakTransitionResult> RejectBreakAsync(string id, string approverId, string? reviewNotes)
    {
        var (brk, error) = await AuthorizeBreakAsync(id, approverId);
        if (error is not null) return error;

        var cleanedReviewNotes = Clean(reviewNotes);
        if (cleanedReviewNotes is null)
            return new AttendanceBreakTransitionResult(true, false, null,
                "Enter a rejection remark before rejecting this break.");

        var now = DateTime.UtcNow;
        brk!.ApprovalStatus = AttendanceApprovalStatus.REJECTED;
        brk.ReviewNotes = cleanedReviewNotes;
        brk.DecidedAt = now;
        brk.UpdatedAt = now;
        await _breaks.UpdateAsync(brk);
        return new AttendanceBreakTransitionResult(true, true, ToBreakDto(brk));
    }

    public async Task<IEnumerable<AttendanceBreakDto>> GetTeamBreakApprovalsAsync(string userId)
    {
        var all = await _breaks.GetPendingAsync();
        var visible = new List<AttendanceBreak>();
        foreach (var brk in all)
        {
            var approvers = await _router.CurrentApproversAsync(Module, brk.EmployeeId, brk.CurrentStep);
            if (approvers.Contains(userId)) visible.Add(brk);
        }

        return visible.Select(ToBreakDto);
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
        return new AttendanceBreakListResult(true, true, breaks.Select(ToBreakDto));
    }

    private async Task<(AttendanceBreak? Break, AttendanceBreakTransitionResult? Error)> AuthorizeBreakAsync(
        string id,
        string approverId)
    {
        var brk = await _breaks.GetByIdAsync(id);
        if (brk is null)
            return (null, new AttendanceBreakTransitionResult(false, false, null));

        var approvers = await _router.CurrentApproversAsync(Module, brk.EmployeeId, brk.CurrentStep);
        if (!approvers.Contains(approverId))
            return (null, new AttendanceBreakTransitionResult(false, false, null));

        if (brk.ApprovalStatus != AttendanceApprovalStatus.PENDING)
            return (brk, new AttendanceBreakTransitionResult(true, false, ToBreakDto(brk),
                "Only pending breaks can be approved or rejected."));

        return (brk, null);
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

    private async Task<int> GetRadiusAsync()
    {
        var orgId = _currentUser.OrganizationId;
        if (string.IsNullOrEmpty(orgId)) return Geo.DefaultRadiusMeters;
        var org = await _organizations.GetByIdAsync(orgId);
        return org?.GeofenceRadiusMeters ?? Geo.DefaultRadiusMeters;
    }

    private static bool OffSiteProofMissing(string? remark, string? photoUrl) =>
        string.IsNullOrWhiteSpace(remark) || string.IsNullOrEmpty(photoUrl);

    private async Task<(AttendanceRecord? Record, AttendanceTransitionResult? Error)> AuthorizeAsync(
        string id,
        string approverId)
    {
        var record = await _repo.GetByIdAsync(id);
        if (record is null)
            return (null, new AttendanceTransitionResult(false, false, null));

        var approvers = await _router.CurrentApproversAsync(Module, record.EmployeeId, record.CurrentStep);
        if (!approvers.Contains(approverId))
            return (null, new AttendanceTransitionResult(false, false, null));

        if (record.ApprovalStatus != AttendanceApprovalStatus.PENDING)
            return (record, new AttendanceTransitionResult(true, false, ToDto(record),
                "Only pending attendance records can be approved or rejected."));

        return (record, null);
    }

    private static AttendanceActionResult OffSiteRequired(double? distance) => new(
        false,
        null,
        "You're outside the project geofence. Add a remark and a photo to clock in from here.",
        OffSiteCode,
        distance);

    // Stored instants are UTC; MySQL drops the Kind, so re-stamp it before
    // formatting so the JSON carries the trailing "Z".
    private static string? Iso(DateTime? d) =>
        d is null ? null : DateTime.SpecifyKind(d.Value, DateTimeKind.Utc).ToString("o");

    private static string? Clean(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static AttendanceRecordDto ToDto(AttendanceRecord r) => new()
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
        ApprovalStatus = r.ApprovalStatus,
        CurrentStep = r.CurrentStep,
        Notes = r.Notes,
        Remark = r.Remark,
        ReviewNotes = r.ReviewNotes,
        SubmittedAt = Iso(r.SubmittedAt),
        DecidedAt = Iso(r.DecidedAt),
        CreatedAt = Iso(r.CreatedAt) ?? string.Empty,
        UpdatedAt = Iso(r.UpdatedAt) ?? string.Empty,
    };

    private static AttendanceBreakDto ToBreakDto(AttendanceBreak b) => new()
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
        ApprovalStatus = b.ApprovalStatus,
        CurrentStep = b.CurrentStep,
        ReviewNotes = b.ReviewNotes,
        SubmittedAt = Iso(b.SubmittedAt),
        DecidedAt = Iso(b.DecidedAt),
    };
}
