using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Payroll.Dtos;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

public class EmployeePayrollService : IEmployeePayrollService
{
    private readonly IPayslipRepository _payslips;
    private readonly IEmployeeProfileRepository _profiles;
    private readonly IStatutoryFileService _statutory;
    private readonly ICurrentUser _currentUser;

    public EmployeePayrollService(
        IPayslipRepository payslips,
        IEmployeeProfileRepository profiles,
        IStatutoryFileService statutory,
        ICurrentUser currentUser)
    {
        _payslips = payslips;
        _profiles = profiles;
        _statutory = statutory;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<EmployeePayslipSummaryDto>> GetMyPayslipsAsync()
    {
        var profileId = await MyProfileIdAsync();
        if (profileId is null) return [];

        var rows = await _payslips.GetForEmployeeAsync(profileId);

        return [.. rows.Select(row => new EmployeePayslipSummaryDto
        {
            Id = row.Payslip.Id,
            PeriodYear = row.Run.PeriodYear,
            PeriodMonth = row.Run.PeriodMonth,
            PeriodLabel = PayrollPeriodLabel.For(row.Run.PeriodYear, row.Run.PeriodMonth),
            GrossPay = row.Payslip.GrossPay,
            NetPay = row.Payslip.NetPay,
            EpfEmployee = row.Payslip.EpfEmployee,
            SocsoEmployee = row.Payslip.SocsoEmployee,
            EisEmployee = row.Payslip.EisEmployee,
            Pcb = row.Payslip.Pcb,
            SubmittedAt = row.Run.SubmittedAt,
        })];
    }

    public async Task<PayslipDto?> GetMyPayslipAsync(string payslipId)
    {
        var payslip = await MineAsync(payslipId);
        if (payslip is null) return null;

        return PayslipMapper.ToDto(payslip, await _payslips.GetLineItemsAsync(payslip.Id));
    }

    public async Task<StatutoryFileResult> RenderMyPayslipPdfAsync(string payslipId)
    {
        var payslip = await MineAsync(payslipId);

        // Same not-found answer as the JSON read, for the same reason.
        if (payslip is null) return new StatutoryFileResult(false, null, null, null, null);

        return await _statutory.RenderPayslipPdfAsync(payslip.PayrollRunId, payslip.EmployeeProfileId);
    }

    // ─── Scoping ────────────────────────────────────────────────────────

    // The three ways this can fail — no such payslip, not the caller's, run
    // not submitted — all return null on purpose. Distinguishing them would
    // tell someone probing ids that a payslip exists, which is already more
    // than they should learn.
    private async Task<Payslip?> MineAsync(string payslipId)
    {
        var profileId = await MyProfileIdAsync();
        if (profileId is null) return null;

        var pair = await _payslips.GetWithRunAsync(payslipId);
        if (pair is null) return null;

        var (payslip, run) = pair.Value;

        if (!string.Equals(payslip.EmployeeProfileId, profileId, StringComparison.Ordinal))
        {
            return null;
        }

        return run.Status == PayrollRunStatus.SUBMITTED ? payslip : null;
    }

    // The caller's OWN profile in the current org, from the token. Never an id
    // off the request — that is the whole boundary this service exists to hold.
    private async Task<string?> MyProfileIdAsync()
    {
        var userId = _currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId)) return null;

        return (await _profiles.GetByUserAsync(userId))?.Id;
    }
}
