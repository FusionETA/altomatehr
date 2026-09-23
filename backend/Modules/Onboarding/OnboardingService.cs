using AltomateHR.Api.Modules.Leave;
using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Modules.Policies;

namespace AltomateHR.Api.Modules.Onboarding;

// Provisioning-time configuration, applied in one call.
//
// Why this exists rather than the caller making five PATCHes: the OT FAN-OUT.
// A new org is seeded with TWO policies, Monthly Workers and Hourly Workers.
// Writing overtime to the default alone leaves hourly staff on statutory rates
// while monthly staff get the client's — wrong payslips, with nothing on screen
// to say so. This applies overtime to EVERY non-archived policy.
//
// That makes it onboarding-only. After go-live use PUT /policies/{id}:
// re-sending this would flatten a group an admin had deliberately diverged.
//
// NOT ATOMIC, and idempotent by design. Blocks apply in a fixed order with no
// surrounding transaction; a partial result names what landed and the caller
// re-sends the same body. A transaction spanning four modules would be a bigger
// promise than the data needs, and the retry is cheap.
public interface IOnboardingService
{
    Task<OnboardingResult> ApplyAsync(OnboardingDto dto);
}

public class OnboardingService : IOnboardingService
{
    private static readonly IReadOnlyDictionary<string, int> Weekdays =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["MONDAY"] = 1, ["TUESDAY"] = 2, ["WEDNESDAY"] = 3, ["THURSDAY"] = 4,
            ["FRIDAY"] = 5, ["SATURDAY"] = 6, ["SUNDAY"] = 7,
        };

    private readonly IOrganizationRepository _organizations;
    private readonly IEmployeePolicyRepository _policies;
    private readonly ILeaveTypeRepository _leaveTypes;
    private readonly IPayrollSettingsRepository _payrollSettings;
    private readonly ICurrentUser _currentUser;

    public OnboardingService(
        IOrganizationRepository organizations,
        IEmployeePolicyRepository policies,
        ILeaveTypeRepository leaveTypes,
        IPayrollSettingsRepository payrollSettings,
        ICurrentUser currentUser)
    {
        _organizations = organizations;
        _policies = policies;
        _leaveTypes = leaveTypes;
        _payrollSettings = payrollSettings;
        _currentUser = currentUser;
    }

    private async Task<Organizations.Entities.Organization> CurrentOrgAsync() =>
        await _organizations.GetByIdAsync(_currentUser.OrganizationId ?? string.Empty)
            ?? throw new InvalidOperationException("No current organization.");

    public async Task<OnboardingResult> ApplyAsync(OnboardingDto dto)
    {
        // Validated BEFORE any write. A leave code nobody recognises must not
        // land after the overtime block already changed every policy — the
        // caller would have no way to tell how far it got.
        if (Validate(dto) is { } error)
            return new OnboardingResult([], [], [], [], error);

        var applied = new List<string>();
        var failed = new List<string>();
        var policiesUpdated = new List<string>();
        var leaveTypesUpdated = new List<string>();

        if (dto.Settings is not null)
            await RunAsync("settings", applied, failed, () => ApplySettingsAsync(dto.Settings));

        if (dto.Calculation is not null)
            await RunAsync("calculation", applied, failed, () => ApplyCalculationAsync(dto.Calculation));

        if (dto.Overtime is not null)
            await RunAsync("overtime", applied, failed,
                async () => policiesUpdated.AddRange(await ApplyOvertimeAsync(dto.Overtime)));

        if (dto.WorkSchedule is not null)
            await RunAsync("workSchedule", applied, failed, () => ApplyWorkScheduleAsync(dto.WorkSchedule));

        if (dto.Leave is not null)
            await RunAsync("leave", applied, failed,
                async () => leaveTypesUpdated.AddRange(await ApplyLeaveAsync(dto.Leave)));

        return new OnboardingResult(applied, failed, policiesUpdated, leaveTypesUpdated);
    }

    // One block failing must not abandon the rest: they are independent, and a
    // caller who retries wants the untouched ones applied, not re-attempted.
    private static async Task RunAsync(
        string block, List<string> applied, List<string> failed, Func<Task> apply)
    {
        try
        {
            await apply();
            applied.Add(block);
        }
        catch
        {
            failed.Add(block);
        }
    }

    // ─── Validation ─────────────────────────────────────────────────────

    private string? Validate(OnboardingDto dto)
    {
        if (dto.Settings is { WorkingDays: not null, NonWorkingDays: not null })
            return "Send workingDays or nonWorkingDays, never both — they write the same setting.";

        foreach (var day in (dto.Settings?.WorkingDays ?? []).Concat(dto.Settings?.NonWorkingDays ?? []))
        {
            if (!Weekdays.ContainsKey(day))
                return $"\"{day}\" is not a weekday name.";
        }

        if (dto.Calculation?.Hrdf is { Contribute: true } hrdf && hrdf.Rate is not > 0)
            return "calculation.hrdf.rate must be greater than 0 when contribute is true.";

        if (dto.Calculation?.ProrationBasis is { Length: > 0 } basis
            && !Enum.TryParse<WorkingDaysRule>(basis, ignoreCase: true, out _))
        {
            return $"\"{basis}\" is not a proration basis.";
        }

        foreach (var (code, carry) in dto.Leave?.CarryForward ?? [])
        {
            if (carry.Enabled && carry.ExpiryMonth is null)
                return $"leave.carryForward.{code}.expiryMonth is required when enabled.";
        }

        return null;
    }

    // ─── Blocks ─────────────────────────────────────────────────────────

    private async Task ApplySettingsAsync(OnboardingSettingsDto settings)
    {
        var org = await CurrentOrgAsync();

        // Stored as the days that ARE worked, so a non-working list is
        // inverted here rather than the column meaning two things.
        var working = settings.WorkingDays is not null
            ? settings.WorkingDays.Select(d => Weekdays[d])
            : Weekdays.Values.Except(settings.NonWorkingDays!.Select(d => Weekdays[d]));

        org.WorkingDays = string.Join(',', working.Distinct().OrderBy(d => d));
        await _organizations.UpdateAsync(org);
    }

    private async Task ApplyCalculationAsync(OnboardingCalculationDto calculation)
    {
        // Created on demand: an org provisioned moments ago may have no
        // payroll settings row yet, and the caller should not have to know.
        var settings = await _payrollSettings.GetAsync()
            ?? await _payrollSettings.AddAsync(new PayrollSettings());

        if (calculation.ProrationBasis is { Length: > 0 } basis)
            settings.WorkingDaysRule = Enum.Parse<WorkingDaysRule>(basis, ignoreCase: true);

        if (calculation.Hrdf is { } hrdf)
        {
            if (hrdf.Contribute is { } contribute) settings.HrdfEnabled = contribute;
            if (hrdf.Rate is { } rate) settings.HrdfRate = rate;
        }

        await _payrollSettings.UpdateAsync(settings);
    }

    // THE fan-out. Every non-archived policy, not just the default.
    private async Task<IReadOnlyList<string>> ApplyOvertimeAsync(OnboardingOvertimeDto overtime)
    {
        var updated = new List<string>();

        foreach (var policy in (await _policies.GetAllAsync()).Where(p => !p.IsArchived))
        {
            if (overtime.NormalDay is { } normal) policy.OtRateNormalDay = normal;
            if (overtime.RestDay is { } rest) policy.OtRateRestDay = rest;
            if (overtime.PublicHoliday is { } holiday) policy.OtRatePublicHoliday = holiday;
            if (overtime.RestDayInShift is { } restIn) policy.OtRateRestDayInShift = restIn;
            if (overtime.PublicHolidayInShift is { } holidayIn) policy.OtRatePublicHolidayInShift = holidayIn;

            policy.UpdatedAt = DateTime.UtcNow;
            await _policies.UpdateAsync(policy);
            updated.Add(policy.Id);
        }

        return updated;
    }

    private async Task ApplyWorkScheduleAsync(OnboardingWorkScheduleDto schedule)
    {
        var org = await CurrentOrgAsync();

        if (schedule.WorkingHoursStart is { Length: > 0 } start) org.WorkingHoursStart = start;
        if (schedule.WorkingHoursEnd is { Length: > 0 } end) org.WorkingHoursEnd = end;
        if (schedule.LunchBreakMinutes is { } lunch) org.LunchBreakMinutes = lunch;

        await _organizations.UpdateAsync(org);
    }

    private async Task<IReadOnlyList<string>> ApplyLeaveAsync(OnboardingLeaveDto leave)
    {
        var types = await _leaveTypes.GetAllAsync();
        var byCode = types.ToDictionary(t => t.Code, StringComparer.OrdinalIgnoreCase);
        var updated = new List<string>();

        foreach (var (code, days) in leave.Entitlements ?? [])
        {
            if (!byCode.TryGetValue(code, out var type)) continue;
            type.DefaultDays = days;
            type.UpdatedAt = DateTime.UtcNow;
            await _leaveTypes.UpdateAsync(type);
            updated.Add(type.Code);
        }

        foreach (var (code, carry) in leave.CarryForward ?? [])
        {
            if (!byCode.TryGetValue(code, out var type)) continue;
            type.CarryForward = carry.Enabled;
            // Cleared when switched off, so a later re-enable cannot inherit a
            // stale month nobody chose.
            type.CarryExpiryMonth = carry.Enabled ? carry.ExpiryMonth : null;
            type.MaxCarryForwardDays = carry.Enabled ? carry.MaxDays : null;
            type.UpdatedAt = DateTime.UtcNow;
            await _leaveTypes.UpdateAsync(type);
            if (!updated.Contains(type.Code)) updated.Add(type.Code);
        }

        return updated;
    }
}
