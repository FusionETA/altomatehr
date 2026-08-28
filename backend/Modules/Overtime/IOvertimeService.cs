using AltomateHR.Api.Modules.Overtime.Dtos;

namespace AltomateHR.Api.Modules.Overtime;

public interface IOvertimeService
{
    Task<IEnumerable<OvertimeRequestDto>> GetMineAsync(string userId);
    Task<IEnumerable<OvertimeRequestDto>> GetTeamAsync(string userId);
    Task<OvertimeRequestDto?> GetVisibleByIdAsync(string id, string userId, bool isAdmin);
    Task<OvertimeSubmitResult> SubmitAsync(CreateOvertimeRequestDto dto, string employeeId);
    Task<OvertimeTransitionResult> AttachAfterPhotoAsync(string id, string userId, AttachOvertimeAfterPhotoDto dto);
    Task<OvertimeTransitionResult> DeleteAfterPhotoAsync(string id, string userId);
    Task<OvertimeTransitionResult> ApproveAsync(string id, string approverId);
    Task<OvertimeTransitionResult> RejectAsync(string id, string approverId, string? reviewNotes);
    Task<OvertimeTransitionResult> CancelAsync(string id, string userId);
    Task<OvertimePhotoUploadResult> StorePhotoAsync(OvertimePhotoUpload upload);
    Task<OvertimePhotoFileResult?> GetPhotoForUserAsync(string fileName, string userId, bool isAdmin);
}

public record OvertimeSubmitResult(bool Ok, OvertimeRequestDto? Request, string? Error);

public record OvertimeTransitionResult(
    bool Found,
    bool Transitioned,
    OvertimeRequestDto? Request,
    string? Error = null);
