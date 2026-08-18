using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Overtime.Dtos;
using AltomateHR.Api.Modules.Overtime.Entities;
using AltomateHR.Api.Modules.Teams;

namespace AltomateHR.Api.Modules.Overtime;

public class OvertimeService : IOvertimeService
{
    private const ApprovalModule Module = ApprovalModule.OT;
    private const string PhotoRoutePrefix = "/overtime/photos/";

    private readonly IOvertimeRepository _requests;
    private readonly IOvertimePhotoStorage _photos;
    private readonly ISupervisionService _supervision;
    private readonly IApprovalRouter _router;

    public OvertimeService(
        IOvertimeRepository requests,
        IOvertimePhotoStorage photos,
        ISupervisionService supervision,
        IApprovalRouter router)
    {
        _requests = requests;
        _photos = photos;
        _supervision = supervision;
        _router = router;
    }

    public async Task<IEnumerable<OvertimeRequestDto>> GetMineAsync(string userId) =>
        (await _requests.GetByEmployeeAsync(userId)).Select(ToDto);

    public async Task<IEnumerable<OvertimeRequestDto>> GetTeamAsync(string userId)
    {
        var all = await _requests.GetAllAsync();
        var visible = new List<OvertimeRequest>();
        foreach (var request in all)
        {
            var approvers = await _router.CurrentApproversAsync(Module, request.EmployeeId, request.CurrentStep);
            if (approvers.Contains(userId)) visible.Add(request);
        }

        var emails = await _supervision.GetEmailsAsync(visible.Select(r => r.EmployeeId).Distinct());
        return visible.Select(request =>
        {
            var dto = ToDto(request);
            dto.EmployeeEmail = emails.GetValueOrDefault(request.EmployeeId);
            return dto;
        });
    }

    public async Task<OvertimeRequestDto?> GetVisibleByIdAsync(string id, string userId, bool isAdmin)
    {
        var request = await _requests.GetByIdAsync(id);
        if (request is null) return null;
        return isAdmin || request.EmployeeId == userId ? ToDto(request) : null;
    }

    public async Task<OvertimeSubmitResult> SubmitAsync(CreateOvertimeRequestDto dto, string employeeId)
    {
        var reason = Clean(dto.Reason);
        if (reason is null)
            return new OvertimeSubmitResult(false, null, "Enter the overtime reason.");

        var beforePhotoUrl = Clean(dto.BeforePhotoUrl);
        if (!IsOvertimePhotoUrl(beforePhotoUrl))
            return new OvertimeSubmitResult(false, null, "Attach the before-work photo before submitting overtime.");

        if (dto.WorkDate is null || dto.StartAt is null || dto.EndAt is null)
            return new OvertimeSubmitResult(false, null, "Enter the overtime date, start time, and end time.");

        var startAt = dto.StartAt.Value;
        var endAt = dto.EndAt.Value;
        if (endAt <= startAt)
            return new OvertimeSubmitResult(false, null, "The overtime end time must be after the start time.");

        var requestedMinutes = (int)Math.Round((endAt - startAt).TotalMinutes);
        if (requestedMinutes <= 0)
            return new OvertimeSubmitResult(false, null, "Overtime duration must be greater than zero.");

        var now = DateTime.UtcNow;
        var request = new OvertimeRequest
        {
            EmployeeId = employeeId,
            ProjectId = Clean(dto.ProjectId),
            WorkDate = dto.WorkDate.Value.Date,
            StartAt = startAt,
            EndAt = endAt,
            RequestedMinutes = requestedMinutes,
            Reason = reason,
            BeforePhotoUrl = beforePhotoUrl!,
            Status = OvertimeStatus.PENDING,
            CurrentStep = 0,
            SubmittedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await _requests.AddAsync(request);
        return new OvertimeSubmitResult(true, ToDto(request), null);
    }

    public async Task<OvertimeTransitionResult> AttachAfterPhotoAsync(
        string id,
        string userId,
        AttachOvertimeAfterPhotoDto dto)
    {
        var request = await _requests.GetByIdAsync(id);
        if (request is null || request.EmployeeId != userId)
            return new OvertimeTransitionResult(false, false, null);

        if (request.Status != OvertimeStatus.PENDING)
            return new OvertimeTransitionResult(true, false, ToDto(request),
                "Only pending overtime requests can be updated.");

        var afterPhotoUrl = Clean(dto.AfterPhotoUrl);
        if (!IsOvertimePhotoUrl(afterPhotoUrl))
            return new OvertimeTransitionResult(true, false, ToDto(request),
                "Attach the after-work photo.");

        request.AfterPhotoUrl = afterPhotoUrl;
        request.UpdatedAt = DateTime.UtcNow;
        await _requests.UpdateAsync(request);
        return new OvertimeTransitionResult(true, true, ToDto(request));
    }

    public async Task<OvertimeTransitionResult> ApproveAsync(string id, string approverId)
    {
        var (request, error) = await AuthorizeAsync(id, approverId);
        if (error is not null) return error;

        if (string.IsNullOrWhiteSpace(request!.AfterPhotoUrl))
            return new OvertimeTransitionResult(true, false, ToDto(request),
                "The after-work photo must be attached before approval.");

        var now = DateTime.UtcNow;
        var stepCount = await _router.StepCountAsync(Module, request.EmployeeId);
        var isFinal = request.CurrentStep + 1 >= stepCount;
        if (isFinal)
        {
            request.Status = OvertimeStatus.APPROVED;
            request.DecidedAt = now;
        }
        else
        {
            request.CurrentStep += 1;
        }

        request.UpdatedAt = now;
        await _requests.UpdateAsync(request);
        return new OvertimeTransitionResult(true, true, ToDto(request));
    }

    public async Task<OvertimeTransitionResult> RejectAsync(string id, string approverId, string? reviewNotes)
    {
        var (request, error) = await AuthorizeAsync(id, approverId);
        if (error is not null) return error;

        var cleanedReviewNotes = Clean(reviewNotes);
        if (cleanedReviewNotes is null)
            return new OvertimeTransitionResult(true, false, null,
                "Enter a rejection remark before rejecting this overtime request.");

        var now = DateTime.UtcNow;
        request!.Status = OvertimeStatus.REJECTED;
        request.ReviewNotes = cleanedReviewNotes;
        request.DecidedAt = now;
        request.UpdatedAt = now;
        await _requests.UpdateAsync(request);
        return new OvertimeTransitionResult(true, true, ToDto(request));
    }

    public async Task<OvertimeTransitionResult> CancelAsync(string id, string userId)
    {
        var request = await _requests.GetByIdAsync(id);
        if (request is null || request.EmployeeId != userId)
            return new OvertimeTransitionResult(false, false, null);

        if (request.Status != OvertimeStatus.PENDING)
            return new OvertimeTransitionResult(true, false, ToDto(request),
                "Only pending overtime requests can be cancelled.");

        request.Status = OvertimeStatus.CANCELLED;
        request.UpdatedAt = DateTime.UtcNow;
        await _requests.UpdateAsync(request);
        return new OvertimeTransitionResult(true, true, ToDto(request));
    }

    public Task<OvertimePhotoUploadResult> StorePhotoAsync(OvertimePhotoUpload upload) =>
        _photos.StoreAsync(upload);

    public async Task<OvertimePhotoFileResult?> GetPhotoForUserAsync(
        string fileName,
        string userId,
        bool isAdmin)
    {
        var photoUrl = $"{PhotoRoutePrefix}{fileName}";
        var request = await _requests.GetByPhotoUrlAsync(photoUrl);
        if (request is null)
            return null;

        if (!isAdmin && request.EmployeeId != userId)
        {
            var approvers = await _router.CurrentApproversAsync(Module, request.EmployeeId, request.CurrentStep);
            if (!approvers.Contains(userId)) return null;
        }

        return await _photos.GetAsync(fileName);
    }

    private async Task<(OvertimeRequest? Request, OvertimeTransitionResult? Error)> AuthorizeAsync(
        string id,
        string approverId)
    {
        var request = await _requests.GetByIdAsync(id);
        if (request is null)
            return (null, new OvertimeTransitionResult(false, false, null));

        var approvers = await _router.CurrentApproversAsync(Module, request.EmployeeId, request.CurrentStep);
        if (!approvers.Contains(approverId))
            return (null, new OvertimeTransitionResult(false, false, null));

        if (request.Status != OvertimeStatus.PENDING)
            return (request, new OvertimeTransitionResult(true, false, ToDto(request),
                "Only pending overtime requests can be approved or rejected."));

        return (request, null);
    }

    private static bool IsOvertimePhotoUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url) && url.StartsWith(PhotoRoutePrefix, StringComparison.Ordinal);

    private static string? Clean(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static string? Iso(DateTime? d) =>
        d is null ? null : DateTime.SpecifyKind(d.Value, DateTimeKind.Utc).ToString("o");

    private static OvertimeRequestDto ToDto(OvertimeRequest request) => new()
    {
        Id = request.Id,
        EmployeeId = request.EmployeeId,
        ProjectId = request.ProjectId,
        WorkDate = request.WorkDate.ToString("yyyy-MM-dd"),
        StartAt = Iso(request.StartAt) ?? string.Empty,
        EndAt = Iso(request.EndAt) ?? string.Empty,
        RequestedMinutes = request.RequestedMinutes,
        Reason = request.Reason,
        BeforePhotoUrl = request.BeforePhotoUrl,
        AfterPhotoUrl = request.AfterPhotoUrl,
        Status = request.Status,
        CurrentStep = request.CurrentStep,
        ReviewNotes = request.ReviewNotes,
        SubmittedAt = Iso(request.SubmittedAt) ?? string.Empty,
        DecidedAt = Iso(request.DecidedAt),
        CreatedAt = Iso(request.CreatedAt) ?? string.Empty,
        UpdatedAt = Iso(request.UpdatedAt) ?? string.Empty,
    };
}
