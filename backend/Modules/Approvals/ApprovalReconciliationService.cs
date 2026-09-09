using AltomateHR.Api.Modules.Approvals.Dtos;
using AltomateHR.Api.Modules.Attendance;
using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Claims;
using AltomateHR.Api.Modules.Leave;
using AltomateHR.Api.Modules.Overtime;

namespace AltomateHR.Api.Modules.Approvals;

// Asks each module to reconcile its own rows and adds up the answers.
//
// It doesn't reach into any module's data: each one owns the rules for what its
// records mean when approved (attendance applies a pending time adjustment,
// leave writes an audit trail entry, overtime refuses while the after-work
// photo is missing), and those rules live with the module, not here.
public class ApprovalReconciliationService : IApprovalReconciliationService
{
    private readonly IAttendanceService _attendance;
    private readonly ILeaveService _leave;
    private readonly IClaimsService _claims;
    private readonly IOvertimeService _overtime;
    private readonly IAuditService _audit;

    public ApprovalReconciliationService(
        IAttendanceService attendance,
        ILeaveService leave,
        IClaimsService claims,
        IOvertimeService overtime,
        IAuditService audit)
    {
        _attendance = attendance;
        _leave = leave;
        _claims = claims;
        _overtime = overtime;
        _audit = audit;
    }

    public async Task<ApprovalReconciliationDto> RunAsync(bool apply)
    {
        var byModule = new Dictionary<string, int>
        {
            ["ATTENDANCE"] = await _attendance.ReconcileUnreachableApprovalsAsync(apply),
            ["LEAVE"] = await _leave.ReconcileUnreachableApprovalsAsync(apply),
            ["CLAIMS"] = await _claims.ReconcileUnreachableApprovalsAsync(apply),
            ["OT"] = await _overtime.ReconcileUnreachableApprovalsAsync(apply),
        };

        var total = byModule.Values.Sum();

        // Only the real thing. A dry run reads and changes nothing, so logging
        // it would fill the feed with rows that mean "somebody looked".
        if (apply && total > 0)
        {
            await _audit.WriteAsync(new AuditEvent(
                AuditActions.ApprovalsReconcile,
                $"Resolved {total} request(s) that had no approver left — "
                    + string.Join(", ", byModule.Where(m => m.Value > 0).Select(m => $"{m.Value} {m.Key}")),
                TargetType: "Approvals",
                Metadata: byModule));
        }

        return new ApprovalReconciliationDto(apply, total, byModule);
    }
}
