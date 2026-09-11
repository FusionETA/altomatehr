using System.Text.Json;
using AltomateHR.Api.Modules.Attendance;
using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Payroll.Dtos;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Modules.Policies;
using AltomateHR.Api.Modules.Policies.Entities;

namespace AltomateHR.Api.Modules.Payroll;

public class PayrollRunAdjustmentService : IPayrollRunAdjustmentService
{
    private readonly IPayrollRunAdjustmentRepository _adjustments;
    private readonly IPayrollRunRepository _runs;
    private readonly IDirectoryService _directory;
    private readonly IPolicyService _policies;
    private readonly IHoursSummaryService _hours;
    private readonly IEmployeeLoanService _loans;
    private readonly IAuditService _audit;

    public PayrollRunAdjustmentService(
        IPayrollRunAdjustmentRepository adjustments,
        IPayrollRunRepository runs,
        IDirectoryService directory,
        IPolicyService policies,
        IHoursSummaryService hours,
        IEmployeeLoanService loans,
        IAuditService audit)
    {
        _adjustments = adjustments;
        _runs = runs;
        _directory = directory;
        _policies = policies;
        _hours = hours;
        _loans = loans;
        _audit = audit;
    }

    // Assembled to match exactly what PayrollRunService.BuildInput will do at
    // generation time. Anything shown here that generation then decides
    // differently is worse than showing nothing — the admin would type against
    // a preview that does not hold.
    public async Task<PayrollAdjustmentContextDto?> GetContextAsync(
        string runId, string employeeProfileId)
    {
        var run = await _runs.GetByIdAsync(runId);
        if (run is null) return null;

        var profile = (await _directory.GetProfilesForCurrentOrgAsync())
            .FirstOrDefault(p => p.Id == employeeProfileId);
        if (profile is null) return null;

        var user = await _directory.GetUserAsync(profile.UserId);
        var policy = await _policies.GetEffectivePolicyAsync(profile.UserId);

        var periodStart = new DateTime(run.PeriodYear, run.PeriodMonth, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);

        var hours = (await _hours.GetHoursForEmployeesAsync([profile.UserId], periodStart, periodEnd))
            .GetValueOrDefault(profile.UserId);

        // Same gate as generation: attendance figures only mean anything when
        // the policy puts the employee on attendance at all.
        var attendanceApplies = policy?.CanAccessAttendance == true && hours is not null;

        // Cash overtime, minus the `adjustment is not null` clause — that one
        // only says whether hours have been TYPED yet, which is what this form
        // is for. What matters here is whether the policy would pay them.
        var cashOt = policy is null || (policy.OtEnabled && policy.OtMethod == OtMethod.CASH);

        var loan = (await _loans.GetRepaymentsForPeriodAsync(run.PeriodYear, run.PeriodMonth))
            .GetValueOrDefault(profile.Id);

        var adjustment = await _adjustments.GetAsync(runId, employeeProfileId);

        return new PayrollAdjustmentContextDto
        {
            EmployeeProfileId = profile.Id,
            EmployeeName = user?.Name ?? user?.Email ?? string.Empty,
            SalaryType = profile.SalaryType,
            Adjustment = adjustment is null ? null : ToDto(adjustment),
            FixedAllowances = ParseFixedAllowances(profile.FixedAllowancesJson),

            // NORMAL minutes only — anything past the shift is overtime and
            // becomes money through the OT fields, never twice.
            AutoWorkedHours = attendanceApplies ? hours!.NormalMin / 60m : null,
            AutoExpectedHours = attendanceApplies && profile.SalaryType == SalaryType.MONTHLY
                ? hours!.ExpectedMin / 60m
                : null,
            AttendanceApplies = attendanceApplies,

            CashOvertime = cashOt,
            OvertimeDisabledReason = cashOt
                ? null
                : policy!.OtEnabled
                    ? "This employee's policy banks overtime as time off, so hours typed here are not paid in cash."
                    : "This employee's policy has overtime switched off, so hours typed here are not paid.",

            LoanInstallments = loan > 0m
                ? [new LoanInstallmentPreviewDto
                    {
                        LoanId = string.Empty,
                        Label = "Loan repayment",
                        Amount = loan,
                    }]
                : [],

            Editable = run.Status == PayrollRunStatus.DRAFT,
        };
    }

    // The profile's recurring rows, carrying the array index the override
    // dictionary keys on. A row that fails to parse is skipped rather than
    // taking the form down — the JSON comes from an older system.
    private static IReadOnlyList<FixedAllowanceRowDto> ParseFixedAllowances(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            var rows = JsonSerializer.Deserialize<List<FixedAllowance>>(
                json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return rows is null
                ? []
                : [.. rows.Select((row, index) => new FixedAllowanceRowDto
                    {
                        Index = index,
                        Category = row.Category,
                        Name = row.Name,
                        Amount = row.Amount,
                        TreatAsRecurring = row.TreatAsRecurring,
                    })];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public async Task<List<PayrollRunAdjustmentDto>> GetForRunAsync(string runId) =>
        (await _adjustments.GetForRunAsync(runId)).Select(ToDto).ToList();

    public async Task<PayrollRunAdjustmentDto?> GetAsync(string runId, string employeeProfileId)
    {
        var row = await _adjustments.GetAsync(runId, employeeProfileId);
        return row is null ? null : ToDto(row);
    }

    public async Task<PayrollRunAdjustmentSaveResult> SaveAsync(
        string runId, string employeeProfileId, SavePayrollRunAdjustmentDto dto)
    {
        var run = await _runs.GetByIdAsync(runId);
        if (run is null) return new PayrollRunAdjustmentSaveResult(false, false, null, null);

        // A submitted run's figures have been filed. Editing its inputs would
        // either do nothing (generation is blocked too) or, worse, look as
        // though it had.
        if (run.Status != PayrollRunStatus.DRAFT)
        {
            return new PayrollRunAdjustmentSaveResult(
                true, false, null,
                "This run is no longer a draft — revert it before editing adjustments.");
        }

        // An unrecognised category is skipped silently by the calculator, so
        // catching it here is the difference between a 400 and an admin's row
        // vanishing without explanation at generation time.
        var items = new List<ManualLineItem>();
        foreach (var item in dto.ManualLineItems)
        {
            var meta = PayrollAdjustmentCategories.Find(item.Category);
            if (meta is null)
            {
                return new PayrollRunAdjustmentSaveResult(
                    true, false, null, $"Unknown adjustment category '{item.Category}'.");
            }

            items.Add(new ManualLineItem
            {
                // Derived, never taken from the client: one source of truth for
                // what this row does to the money.
                Kind = meta.Kind,
                Category = meta.Code,
                Label = string.IsNullOrWhiteSpace(item.Label) ? null : item.Label.Trim(),
                Amount = item.Amount,
                TreatAsRecurring = item.TreatAsRecurring,
            });
        }

        var overrides = dto.FixedAllowanceOverrides.ToDictionary(
            kv => kv.Key,
            kv => new FixedAllowanceOverride { Amount = kv.Value.Amount, Skip = kv.Value.Skip },
            StringComparer.Ordinal);

        var saved = await _adjustments.UpsertAsync(new PayrollRunAdjustment
        {
            PayrollRunId = runId,
            EmployeeProfileId = employeeProfileId,
            OtNormalHours = dto.OtNormalHours,
            OtRestHours = dto.OtRestHours,
            OtPublicHours = dto.OtPublicHours,
            ManualLineItemsJson = PayrollRunAdjustments.Serialize(items),
            FixedAllowanceOverridesJson = PayrollRunAdjustments.Serialize(overrides),
            WorkedHours = dto.WorkedHours,
            ExpectedHours = dto.ExpectedHours,
            Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim(),
        });

        // The payslips were built from the old inputs, so they are now behind.
        await _runs.MarkMutatedAsync(runId);

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.PayrollRunAdjustmentSave,
            "Saved a payroll adjustment",
            TargetType: "PayrollRunAdjustment",
            TargetId: saved.Id,
            Metadata: new
            {
                PayrollRunId = runId,
                EmployeeProfileId = employeeProfileId,
                saved.OtNormalHours,
                saved.OtRestHours,
                saved.OtPublicHours,
                ManualLineItemCount = items.Count,
                OverrideCount = overrides.Count,
            }));

        return new PayrollRunAdjustmentSaveResult(true, true, ToDto(saved), null);
    }

    public async Task<PayrollRunAdjustmentSaveResult> ClearAsync(string runId, string employeeProfileId)
    {
        var run = await _runs.GetByIdAsync(runId);
        if (run is null) return new PayrollRunAdjustmentSaveResult(false, false, null, null);

        if (run.Status != PayrollRunStatus.DRAFT)
        {
            return new PayrollRunAdjustmentSaveResult(
                true, false, null,
                "This run is no longer a draft — revert it before clearing adjustments.");
        }

        var removed = await _adjustments.DeleteAsync(runId, employeeProfileId);

        // Nothing to clear is not an error — the caller wanted no adjustment on
        // this employee and there is none. But the run only goes stale if
        // something actually changed.
        if (!removed) return new PayrollRunAdjustmentSaveResult(true, true, null, null);

        await _runs.MarkMutatedAsync(runId);

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.PayrollRunAdjustmentClear,
            "Cleared a payroll adjustment",
            TargetType: "PayrollRun",
            TargetId: runId,
            Metadata: new { PayrollRunId = runId, EmployeeProfileId = employeeProfileId }));

        return new PayrollRunAdjustmentSaveResult(true, true, null, null);
    }

    // ─── Mapping ────────────────────────────────────────────────────────

    private static PayrollRunAdjustmentDto ToDto(PayrollRunAdjustment a) => new()
    {
        Id = a.Id,
        PayrollRunId = a.PayrollRunId,
        EmployeeProfileId = a.EmployeeProfileId,
        OtNormalHours = a.OtNormalHours,
        OtRestHours = a.OtRestHours,
        OtPublicHours = a.OtPublicHours,
        ManualLineItems = PayrollRunAdjustments
            .ParseManualLineItems(a.ManualLineItemsJson)
            .Select(li => new ManualLineItemResponseDto
            {
                Kind = li.Kind,
                Category = li.Category,
                Label = li.Label,
                Amount = li.Amount,
                TreatAsRecurring = li.TreatAsRecurring,
            })
            .ToList(),
        FixedAllowanceOverrides = PayrollRunAdjustments
            .ParseOverrides(a.FixedAllowanceOverridesJson)
            .ToDictionary(
                kv => kv.Key,
                kv => new FixedAllowanceOverrideDto { Amount = kv.Value.Amount, Skip = kv.Value.Skip },
                StringComparer.Ordinal),
        WorkedHours = a.WorkedHours,
        ExpectedHours = a.ExpectedHours,
        Notes = a.Notes,
        CreatedAt = a.CreatedAt,
        UpdatedAt = a.UpdatedAt,
    };
}
