using AltomateHR.Api.Modules.Overtime.Dtos;
using AltomateHR.Api.Modules.Overtime.Entities;

namespace AltomateHR.Api.Modules.Overtime;

public interface IOvertimeService
{
    Task<IEnumerable<OvertimeRequestDto>> GetMineAsync(string userId);
    Task<IEnumerable<OvertimeRequestDto>> GetTeamAsync(string userId);

    // Every overtime request in the org, for the admin attendance view.
    //
    // Deliberately not GetTeamAsync: that one is approver-scoped, and an admin
    // is never in an approval chain (OrgRoles.IsAdministrative), so it would
    // hand an admin an empty list. Oversight reads the whole org; the tenant
    // filter keeps "whole org" honest.
    Task<IEnumerable<OvertimeRequestDto>> GetAllForAdminAsync();
    Task<OvertimeRequestDto?> GetVisibleByIdAsync(string id, string userId, bool isAdmin);
    Task<OvertimeSubmitResult> SubmitAsync(CreateOvertimeRequestDto dto, string employeeId);
    Task<OvertimeTransitionResult> AttachAfterPhotoAsync(string id, string userId, AttachOvertimeAfterPhotoDto dto);
    Task<OvertimeTransitionResult> DeleteAfterPhotoAsync(string id, string userId);
    Task<OvertimeTransitionResult> ApproveAsync(string id, string approverId);

    // Sign off many requests at once, as the current-step approver of each.
    // Independent per-id success/failure, so the result is a report rather than
    // a single verdict. No bulk REJECT counterpart on purpose — see
    // OvertimeService.BulkApproveAsync.
    Task<OvertimeBulkResult> BulkApproveAsync(IReadOnlyList<string> ids, string approverId);
    Task<OvertimeTransitionResult> RejectAsync(string id, string approverId, string? reviewNotes);
    Task<OvertimeTransitionResult> CancelAsync(string id, string userId);
    Task<OvertimePhotoUploadResult> StorePhotoAsync(OvertimePhotoUpload upload);
    Task<OvertimePhotoFileResult?> GetPhotoForUserAsync(string fileName, string userId, bool isAdmin);

    // One employee's overtime requests as entities, for the hours summary.
    Task<IReadOnlyList<OvertimeRequest>> GetByEmployeeAsync(string employeeId);

    // Resolves PENDING items that no longer have any approver to route to.
    // `apply: false` only counts them. See the implementation for why.
    Task<int> ReconcileUnreachableApprovalsAsync(bool apply);
}

public record OvertimeSubmitResult(bool Ok, OvertimeRequestDto? Request, string? Error);

public record OvertimeTransitionResult(
    bool Found,
    bool Transitioned,
    OvertimeRequestDto? Request,
    string? Error = null);

// Mirrors the claims and attendance bulk contract: per-id success/failure, so a
// run where eighteen of twenty landed reports as exactly that instead of as a
// failed request.
public record OvertimeBulkResultItem(string Id, bool Ok, string? Error = null);

public record OvertimeBulkResult(int Succeeded, int Failed, IReadOnlyList<OvertimeBulkResultItem> Items);
