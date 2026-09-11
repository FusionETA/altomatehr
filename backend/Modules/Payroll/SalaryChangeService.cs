using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Payroll.Dtos;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Modules.Policies.Entities;

namespace AltomateHR.Api.Modules.Payroll;

public class SalaryChangeService : ISalaryChangeService
{
    private readonly ISalaryChangeRepository _changes;
    private readonly IPayrollRunRepository _runs;
    private readonly IPayslipRepository _payslips;
    private readonly IPayrollRunAdjustmentRepository _adjustments;
    private readonly IPayrollSettingsService _settings;
    private readonly IDirectoryService _directory;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;

    public SalaryChangeService(
        ISalaryChangeRepository changes,
        IPayrollRunRepository runs,
        IPayslipRepository payslips,
        IPayrollRunAdjustmentRepository adjustments,
        IPayrollSettingsService settings,
        IDirectoryService directory,
        ICurrentUser currentUser,
        IAuditService audit)
    {
        _changes = changes;
        _runs = runs;
        _payslips = payslips;
        _adjustments = adjustments;
        _settings = settings;
        _directory = directory;
        _currentUser = currentUser;
        _audit = audit;
    }

    public async Task<IReadOnlyList<SalaryChangeDto>> GetForEmployeeAsync(string employeeProfileId)
    {
        var changes = await _changes.GetForEmployeeAsync(employeeProfileId);
        if (changes.Count == 0) return [];

        var names = await NamesAsync();

        return [.. changes.Select(c => ToDto(c, names))];
    }

    // Called when an employee's salary is edited. Returns null when nothing
    // actually moved — recording a no-op change would fill the history that
    // an IR dispute reads with noise.
    public async Task<SalaryChangeDto?> RecordAsync(
        EmployeeProfile before, EmployeeProfile after, RecordSalaryChangeDto dto)
    {
        var unchanged = before.SalaryType == after.SalaryType
            && before.MonthlySalary == after.MonthlySalary
            && before.HourlyRate == after.HourlyRate;

        if (unchanged) return null;

        var change = new SalaryChange
        {
            EmployeeProfileId = after.Id,
            EffectiveDate = dto.EffectiveDate.Date,
            PreviousSalaryType = before.SalaryType,
            PreviousMonthlySalary = before.MonthlySalary,
            PreviousHourlyRate = before.HourlyRate,
            NewSalaryType = after.SalaryType,
            NewMonthlySalary = after.MonthlySalary,
            NewHourlyRate = after.HourlyRate,
            Reason = dto.Reason,
            Notes = dto.Notes,
            ChangedByUserId = _currentUser.UserId,
            CreatedAt = DateTime.UtcNow,
        };

        await _changes.AddAsync(change);

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.PayrollSalaryChange,
            $"Recorded a salary {SalaryChangeHints.ReasonLabel(dto.Reason).ToLowerInvariant()} "
            + $"effective {change.EffectiveDate:d MMM yyyy}",
            TargetType: "SalaryChange",
            TargetId: change.Id,
            Metadata: new
            {
                change.EmployeeProfileId,
                change.PreviousMonthlySalary,
                change.NewMonthlySalary,
                Reason = change.Reason.ToString(),
            }));

        return ToDto(change, await NamesAsync());
    }

    // What a generated run needs correcting for.
    //
    // Deliberately advisory. The engine pays ONE salary for the month, and
    // every Malaysian product this competes with leaves the mid-cycle
    // correction to the admin — they know whether a raise was meant to be
    // backdated and the engine does not. This removes the arithmetic and the
    // chance of forgetting, not the decision.
    public async Task<IReadOnlyList<SalaryChangeHints.Hint>> GetHintsForRunAsync(string runId)
    {
        var run = await _runs.GetByIdAsync(runId);
        if (run is null) return [];

        var payslips = await _payslips.GetForRunAsync(runId);
        if (payslips.Count == 0) return [];

        var periodStart = new DateTime(run.PeriodYear, run.PeriodMonth, 1);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);

        var changes = (await _changes.GetEffectiveInRangeAsync(periodStart, periodEnd))
            .GroupBy(c => c.EmployeeProfileId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        if (changes.Count == 0) return [];

        var settings = await _settings.GetEffectiveAsync();
        var adjustments = (await _adjustments.GetForRunAsync(runId))
            .ToDictionary(a => a.EmployeeProfileId, a => a, StringComparer.Ordinal);

        var hints = new List<SalaryChangeHints.Hint>();

        foreach (var payslip in payslips)
        {
            if (!changes.TryGetValue(payslip.EmployeeProfileId, out var employeeChanges)) continue;

            var labels = ExistingLabels(adjustments.GetValueOrDefault(payslip.EmployeeProfileId));

            foreach (var change in employeeChanges)
            {
                var hint = SalaryChangeHints.Compute(new SalaryChangeHints.Input
                {
                    PayslipId = payslip.Id,
                    EmployeeProfileId = payslip.EmployeeProfileId,
                    EmployeeName = payslip.SnapshotName,
                    PayslipSnapshotMonthlySalary = payslip.SnapshotMonthlySalary ?? 0m,
                    Change = change,
                    PeriodYear = run.PeriodYear,
                    PeriodMonth = run.PeriodMonth,
                    ProrationRule = settings.WorkingDaysRule,
                    ExistingManualLineLabels = labels,
                });

                // MATCHED means the change landed on the 1st, so there is
                // nothing to correct and nothing worth showing.
                if (hint is not null && hint.Outcome != SalaryChangeHints.Scenario.MATCHED)
                {
                    hints.Add(hint);
                }
            }
        }

        return hints;
    }

    private static IReadOnlyList<string> ExistingLabels(PayrollRunAdjustment? adjustment) =>
        adjustment is null
            ? []
            : [.. PayrollRunAdjustments.ParseManualLineItems(adjustment.ManualLineItemsJson)
                .Select(i => i.Label)];

    private async Task<IReadOnlyDictionary<string, string>> NamesAsync()
    {
        var users = (await _directory.GetUsersAsync())
            .ToDictionary(u => u.Id, u => u.Name ?? u.Email, StringComparer.Ordinal);

        return users;
    }

    private static SalaryChangeDto ToDto(
        SalaryChange change, IReadOnlyDictionary<string, string> userNames) => new()
        {
            Id = change.Id,
            EmployeeProfileId = change.EmployeeProfileId,
            EffectiveDate = change.EffectiveDate,
            PreviousSalaryType = change.PreviousSalaryType,
            PreviousMonthlySalary = change.PreviousMonthlySalary,
            PreviousHourlyRate = change.PreviousHourlyRate,
            NewSalaryType = change.NewSalaryType,
            NewMonthlySalary = change.NewMonthlySalary,
            NewHourlyRate = change.NewHourlyRate,
            Reason = change.Reason,
            ReasonLabel = SalaryChangeHints.ReasonLabel(change.Reason),
            RaisePercent = SalaryChangeHints.RaisePercent(change),
            Notes = change.Notes,
            ChangedByUserId = change.ChangedByUserId,
            ChangedByName = change.ChangedByUserId is not null
                && userNames.TryGetValue(change.ChangedByUserId, out var name)
                    ? name
                    : null,
            CreatedAt = change.CreatedAt,
        };
}
