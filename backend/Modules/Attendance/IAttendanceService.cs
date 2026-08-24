using AltomateHR.Api.Modules.Attendance.Dtos;

namespace AltomateHR.Api.Modules.Attendance;

public interface IAttendanceService
{
    Task<AttendanceRecordDto?> GetTodayAsync(string employeeId);
    Task<IEnumerable<AttendanceRecordDto>> GetHistoryAsync(string userId, bool isAdmin);
    Task<IEnumerable<AttendanceRecordDto>> GetTeamApprovalsAsync(string userId);
    Task<AttendanceActionResult> ClockInAsync(string employeeId, ClockInDto dto);
    Task<AttendanceActionResult> ClockOutAsync(string employeeId, ClockOutDto dto);
    Task<AttendanceTransitionResult> ApproveAsync(string id, string approverId);
    Task<AttendanceTransitionResult> RejectAsync(string id, string approverId, string? reviewNotes);
    Task<AttendancePhotoUploadResult> StorePhotoAsync(AttendancePhotoUpload upload);
    Task<AttendancePhotoFileResult?> GetPhotoForUserAsync(string fileName, string userId, bool isAdmin);

    Task<AttendanceBreakActionResult> StartBreakAsync(string employeeId, StartBreakDto dto);
    Task<AttendanceBreakActionResult> EndBreakAsync(string employeeId, EndBreakDto dto);
    Task<AttendanceBreakTransitionResult> ApproveBreakAsync(string id, string approverId);
    Task<AttendanceBreakTransitionResult> RejectBreakAsync(string id, string approverId, string? reviewNotes);
    Task<IEnumerable<AttendanceBreakDto>> GetTeamBreakApprovalsAsync(string userId);
    Task<AttendanceBreakListResult> GetBreaksForRecordAsync(string recordId, string requestingUserId, string? requestingRole);
}

// Ok=false carries a human-readable Error. Code distinguishes the off-site case
// ("OFF_SITE_ACTION_REQUIRED") from ordinary failures so the client can reveal
// the remark + photo UI and retry.
public record AttendanceActionResult(
    bool Ok,
    AttendanceRecordDto? Record,
    string? Error = null,
    string? Code = null,
    double? DistanceMeters = null);

public record AttendanceTransitionResult(
    bool Found,
    bool Transitioned,
    AttendanceRecordDto? Record,
    string? Error = null);

public record AttendanceBreakActionResult(
    bool Ok,
    AttendanceBreakDto? Break,
    string? Error = null,
    string? Code = null);

public record AttendanceBreakTransitionResult(
    bool Found,
    bool Transitioned,
    AttendanceBreakDto? Break,
    string? Error = null);

public record AttendanceBreakListResult(
    bool Found,
    bool Authorized,
    IEnumerable<AttendanceBreakDto>? Breaks,
    string? Error = null);
