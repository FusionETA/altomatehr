using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Payroll.Dtos;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Modules.Payroll.Pdf;

namespace AltomateHR.Api.Modules.Payroll;

public class EmployeePayrollService : IEmployeePayrollService
{
    private readonly IPayslipRepository _payslips;
    private readonly IPayrollRunRepository _runs;
    private readonly IEmployeeProfileRepository _profiles;
    private readonly IPayrollAnnualReportService _annual;
    private readonly IStatutoryFileService _statutory;
    private readonly ICurrentUser _currentUser;
    private readonly ITp1FormService? _tp1;

    public EmployeePayrollService(
        IPayslipRepository payslips,
        IPayrollRunRepository runs,
        IEmployeeProfileRepository profiles,
        IPayrollAnnualReportService annual,
        IStatutoryFileService statutory,
        ICurrentUser currentUser,
        ITp1FormService? tp1 = null)
    {
        _tp1 = tp1;
        _payslips = payslips;
        _runs = runs;
        _profiles = profiles;
        _annual = annual;
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
            // Tax withheld, so Additional PCB is included — matches the detail view.
            Pcb = row.Payslip.Pcb + row.Payslip.VoluntaryPcb,
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

    public async Task<StatutoryFileResult> RenderMyTp1FormAsync(string payslipId)
    {
        var payslip = await MineAsync(payslipId);
        if (payslip is null || _tp1 is null) return new StatutoryFileResult(false, null, null, null, null);

        return await _tp1.RenderFormAsync(payslip.PayrollRunId, payslip.EmployeeProfileId);
    }

    // ─── Form EA ────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<EmployeeEaFormDto>> GetMyEaFormsAsync()
    {
        var profile = await MyProfileAsync();
        if (profile is null) return [];

        // GetForEmployeeAsync returns submitted runs only, so a year paid
        // only in drafts does not appear.
        var lastPaidMonth = (await _payslips.GetForEmployeeAsync(profile.Id))
            .GroupBy(row => row.Run.PeriodYear)
            .ToDictionary(g => g.Key, g => g.Max(row => row.Run.PeriodMonth));

        if (lastPaidMonth.Count == 0) return [];

        // Approval is the company's, not the employee's: a month counts once
        // its run is submitted, whoever was on it.
        var submitted = (await _runs.GetAllAsync())
            .Where(r => r.Status == PayrollRunStatus.SUBMITTED)
            .GroupBy(r => r.PeriodYear)
            .ToDictionary(g => g.Key, g => (IReadOnlyCollection<int>)g.Select(r => r.PeriodMonth).ToHashSet());

        var forms = new List<EmployeeEaFormDto>();

        foreach (var (year, lastPaid) in lastPaidMonth.OrderByDescending(kv => kv.Key))
        {
            var ea = EaYear.For(
                year, profile.LeaveDate, lastPaid, submitted.GetValueOrDefault(year) ?? [],
                await _payslips.HasUnsubmittedForEmployeeAsync(profile.Id, year));

            forms.Add(new EmployeeEaFormDto
            {
                Year = year,
                Available = ea.Ready,
                RequiredMonths = ea.ThroughMonth,
                ApprovedMonths = ea.ApprovedMonths,
            });
        }

        return forms;
    }

    public async Task<StatutoryFileResult> RenderMyEaFormAsync(int year)
    {
        var profileId = await MyProfileIdAsync();
        if (profileId is null) return new StatutoryFileResult(false, null, null, null, null);

        var payload = await _annual.LoadAsync(year);

        // Not paid that year is a 404, the same as someone else's payslip.
        var mine = payload.Employees.FirstOrDefault(e =>
            string.Equals(e.EmployeeProfileId, profileId, StringComparison.Ordinal));
        if (mine is null) return new StatutoryFileResult(false, null, null, null, null);

        // Held here too, not only on the list, so a direct URL cannot fetch a
        // part-year form that under-declares.
        var ea = EaYear.For(
            year, mine.LeaveDate, mine.Months.Max(m => m.Month), payload.SubmittedMonths,
            await _payslips.HasUnsubmittedForEmployeeAsync(profileId, year));

        if (!ea.Ready)
        {
            return StatutoryFileResult.Refused(ea.ThroughMonth == 12
                ? $"Your {year} EA form will be ready once all 12 months of {year} payroll are approved."
                : $"Your {year} EA form will be ready once {year} payroll is approved up to "
                  + $"{System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(ea.ThroughMonth)}.");
        }

        // Only the caller's own page: the bulk form is every employee's pay.
        var bytes = FormEaPdf.Render(payload with { Employees = [mine] });

        return new StatutoryFileResult(true, $"Form_EA_{year}.pdf", bytes, FormEaPdf.ContentType, null);
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
    private async Task<EmployeeProfile?> MyProfileAsync()
    {
        var userId = _currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId)) return null;

        return await _profiles.GetByUserAsync(userId);
    }

    private async Task<string?> MyProfileIdAsync() => (await MyProfileAsync())?.Id;
}
