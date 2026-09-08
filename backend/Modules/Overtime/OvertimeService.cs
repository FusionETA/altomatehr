using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Notifications;
using AltomateHR.Api.Modules.Notifications.Entities;
using AltomateHR.Api.Modules.Overtime.Dtos;
using AltomateHR.Api.Modules.Overtime.Entities;
using AltomateHR.Api.Modules.Teams;

namespace AltomateHR.Api.Modules.Overtime;

public class OvertimeService : IOvertimeService
{
    private const ApprovalModule Module = ApprovalModule.OT;
    private const string PhotoRoutePrefix = "/overtime/photos/";

    // Same cap as the claims and attendance bulk endpoints.
    private const int MaxBulkIds = 200;

    private readonly IOvertimeRepository _requests;
    private readonly IOvertimePhotoStorage _photos;
    private readonly ISupervisionService _supervision;
    private readonly IApprovalRouter _router;
    private readonly INotificationService _notifications;

    public OvertimeService(
        IOvertimeRepository requests,
        IOvertimePhotoStorage photos,
        ISupervisionService supervision,
        IApprovalRouter router,
        INotificationService notifications)
    {
        _requests = requests;
        _photos = photos;
        _supervision = supervision;
        _router = router;
        _notifications = notifications;
    }

    public async Task<IEnumerable<OvertimeRequestDto>> GetMineAsync(string userId) =>
        (await _requests.GetByEmployeeAsync(userId)).Select(ToDto);

    public async Task<IEnumerable<OvertimeRequestDto>> GetTeamAsync(string userId)
    {
        var all = await _requests.GetAllAsync();
        var visible = new List<OvertimeRequest>();
        foreach (var request in all)
        {
            var approvers = await _router.CurrentApproversAsync(Module, request.EmployeeId, request.CurrentStep, request.ProjectId);
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
        await NotifyReviewersAsync(request);
        return new OvertimeSubmitResult(true, ToDto(request), null);
    }

    // Removes the after-work photo from a request and deletes the underlying
    // file. Same guards as attaching it: owner-only, and only while PENDING —
    // once a supervisor has decided, the photo was part of what they reviewed,
    // so it stays. The before-photo is deliberately NOT deletable: it's
    // required on the entity, so removing it would leave the request invalid.
    public async Task<OvertimeTransitionResult> DeleteAfterPhotoAsync(string id, string userId)
    {
        var request = await _requests.GetByIdAsync(id);
        if (request is null || request.EmployeeId != userId)
            return new OvertimeTransitionResult(false, false, null);

        if (request.Status != OvertimeStatus.PENDING)
            return new OvertimeTransitionResult(true, false, ToDto(request),
                "Only pending overtime requests can be updated.");

        if (string.IsNullOrEmpty(request.AfterPhotoUrl))
            return new OvertimeTransitionResult(true, false, ToDto(request),
                "There's no after-work photo to remove.");

        // Clear the reference first: if the file delete fails we'd rather have
        // an orphaned file on disk than a request pointing at a missing photo.
        var fileName = Path.GetFileName(request.AfterPhotoUrl);
        request.AfterPhotoUrl = null;
        request.UpdatedAt = DateTime.UtcNow;
        await _requests.UpdateAsync(request);

        try { await _photos.DeleteAsync(fileName); } catch { /* best-effort */ }

        return new OvertimeTransitionResult(true, true, ToDto(request));
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

        var now = DateTime.UtcNow;
        request.AfterPhotoUrl = afterPhotoUrl;

        // Nobody above them to ask → this completes the request. Unlike the
        // other modules the decision can't happen at submit: ApproveAsync
        // refuses without an after-work photo, and there isn't one until now.
        // So the request waits here, not on an approver who doesn't exist.
        // See OrgRoles — admins are oversight, not a fallback approver.
        if (await _router.StepCountAsync(Module, request.EmployeeId, request.ProjectId) == 0)
        {
            request.Status = OvertimeStatus.APPROVED;
            request.DecidedAt = now;
        }

        request.UpdatedAt = now;
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
        var stepCount = await _router.StepCountAsync(Module, request.EmployeeId, request.ProjectId);
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
        await NotifyDecisionAsync(request, approved: true);
        return new OvertimeTransitionResult(true, true, ToDto(request));
    }

    // Approve many requests in one gesture. Every id is judged on its own: one
    // that isn't the caller's to decide, or that someone else already decided,
    // fails on its own line and never blocks the rest.
    //
    // There is deliberately no bulk REJECT counterpart. Rejection requires a
    // remark, and one remark stapled to a dozen unrelated requests tells each
    // employee nothing about why theirs was refused — so rejection stays one at
    // a time, where the reason can be about that request.
    //
    // A request with no after-work photo is refused WITH ITS REASON rather than
    // silently dropped. ApproveAsync gates on that photo, so these would fail
    // anyway; saying so is what tells the approver to chase the photo instead of
    // wondering why the row is still in their queue.
    public async Task<OvertimeBulkResult> BulkApproveAsync(IReadOnlyList<string> ids, string approverId)
    {
        if (ids.Count > MaxBulkIds)
        {
            return new OvertimeBulkResult(0, ids.Count, [
                new OvertimeBulkResultItem(string.Empty, false, $"Too many requests — pick fewer than {MaxBulkIds}."),
            ]);
        }

        var items = new List<OvertimeBulkResultItem>(ids.Count);

        // Distinct: the same id twice would otherwise count as two successes
        // while only one request moved.
        foreach (var id in ids.Distinct(StringComparer.Ordinal))
        {
            var (request, error) = await AuthorizeAsync(id, approverId);
            if (error is not null)
            {
                items.Add(new OvertimeBulkResultItem(id, false,
                    error.Error ?? "You can't approve this overtime request."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(request!.AfterPhotoUrl))
            {
                items.Add(new OvertimeBulkResultItem(id, false,
                    "The after-work photo must be attached before approval."));
                continue;
            }

            var now = DateTime.UtcNow;
            var stepCount = await _router.StepCountAsync(Module, request.EmployeeId, request.ProjectId);
            if (request.CurrentStep + 1 >= stepCount)
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
            items.Add(new OvertimeBulkResultItem(id, true));
        }

        return new OvertimeBulkResult(items.Count(i => i.Ok), items.Count(i => !i.Ok), items);
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
        await NotifyDecisionAsync(request, approved: false);
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
            var approvers = await _router.CurrentApproversAsync(Module, request.EmployeeId, request.CurrentStep, request.ProjectId);
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

        var approvers = await _router.CurrentApproversAsync(Module, request.EmployeeId, request.CurrentStep, request.ProjectId);
        if (!approvers.Contains(approverId))
            return (null, new OvertimeTransitionResult(false, false, null));

        if (request.Status != OvertimeStatus.PENDING)
            return (request, new OvertimeTransitionResult(true, false, ToDto(request),
                "Only pending overtime requests can be approved or rejected."));

        return (request, null);
    }

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
        var stuck = 0;

        foreach (var request in (await _requests.GetAllAsync()).Where(r => r.Status == OvertimeStatus.PENDING))
        {
            // Waiting on the employee's after-work photo, not on an approver.
            // Approval refuses without it, and reconciliation must not be a way
            // around that.
            if (string.IsNullOrWhiteSpace(request.AfterPhotoUrl)) continue;

            var approvers = await _router.CurrentApproversAsync(Module, request.EmployeeId, request.CurrentStep, request.ProjectId);
            if (approvers.Count > 0) continue;

            stuck++;
            if (!apply) continue;

            request.Status = OvertimeStatus.APPROVED;
            request.DecidedAt = now;
            request.UpdatedAt = now;
            await _requests.UpdateAsync(request);
        }

        return stuck;
    }

    // A newly-submitted request: nudge whoever has to review it. Same shape as
    // Claims/Leave's SUBMITTED notification — no realtime SSE nudge here since
    // Overtime never had one, but the persisted bell entry is the part that was
    // asked for.
    private async Task NotifyReviewersAsync(OvertimeRequest request)
    {
        var approvers = await _router.CurrentApproversAsync(Module, request.EmployeeId, request.CurrentStep, request.ProjectId);
        foreach (var reviewerId in approvers)
        {
            if (string.IsNullOrEmpty(reviewerId)) continue;
            await _notifications.NotifyAsync(
                request.OrganizationId, reviewerId, NotificationType.OVERTIME_SUBMITTED,
                "New overtime request to review",
                $"{request.RequestedMinutes / 60.0:0.#}h of overtime on {request.WorkDate:MMM d} needs your review.",
                "/overtime");
        }
    }

    // Notifies the employee of a decision.
    private async Task NotifyDecisionAsync(OvertimeRequest request, bool approved)
    {
        var title = approved ? "Overtime approved" : "Overtime rejected";
        await _notifications.NotifyAsync(
            request.OrganizationId, request.EmployeeId, NotificationType.OVERTIME_REVIEWED,
            title,
            approved
                ? $"Your overtime request on {request.WorkDate:MMM d} was approved."
                : $"Your overtime request on {request.WorkDate:MMM d} was rejected.{(string.IsNullOrEmpty(request.ReviewNotes) ? "" : $" Reason: {request.ReviewNotes}")}",
            "/overtime");
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

    public async Task<IReadOnlyList<OvertimeRequest>> GetByEmployeeAsync(string employeeId) =>
        await _requests.GetByEmployeeAsync(employeeId);
}
