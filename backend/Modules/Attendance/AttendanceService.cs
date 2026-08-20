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
    private readonly IProjectService _projects;
    private readonly IOrganizationService _organizations;
    private readonly ICurrentUser _currentUser;
    private readonly IAttendancePhotoStorage _photos;
    private readonly IPolicyService _policies;
    private readonly ISupervisionService _supervision;
    private readonly IApprovalRouter _router;

    public AttendanceService(
        IAttendanceRepository repo,
        IProjectService projects,
        IOrganizationService organizations,
        ICurrentUser currentUser,
        IAttendancePhotoStorage photos,
        IPolicyService policies,
        ISupervisionService supervision,
        IApprovalRouter router)
    {
        _repo = repo;
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
}
