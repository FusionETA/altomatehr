using AltomateHR.Api.Modules.Policies.Entities;

namespace AltomateHR.Api.Modules.Payroll;

// Turning OT hours into money.
//
// `OtRateService` already answers WHICH multiplier applies on a given date
// (normal / rest day / public holiday). This supplies the other half — the
// hourly rate to multiply it by.
public static class OvertimePay
{
    // Employment Act s.60I ordinary rate of pay: monthly ÷ (working days × daily
    // hours). HOURLY staff already have a rate, so it is returned as-is.
    //
    // `workingDays` is the s.60I basis from PayrollSettings (26 by convention),
    // NOT the calendar length used for proration.
    public static decimal DeriveHourlyRate(
        SalaryType salaryType,
        decimal? monthlySalary,
        decimal? hourlyRate,
        int workingDays,
        decimal? dailyHours = null)
    {
        if (salaryType == SalaryType.HOURLY) return hourlyRate ?? 0m;
        if (monthlySalary is null || workingDays <= 0) return 0m;

        var hours = dailyHours > 0m ? dailyHours.Value : DefaultDailyHours;

        return monthlySalary.Value / (workingDays * hours);
    }

    // Effective daily working hours: the employee's primary project hours net of
    // the lunch break, falling back to the org's hours when the project doesn't
    // set its own.
    public static decimal DeriveDailyHours(
        string? projectWorkingHoursStart,
        string? projectWorkingHoursEnd,
        int? projectLunchBreakMinutes,
        string orgWorkingHoursStart,
        string orgWorkingHoursEnd,
        int defaultLunchMinutes = 60)
    {
        var start = MinutesFromHhmm(projectWorkingHoursStart ?? orgWorkingHoursStart);
        var end = MinutesFromHhmm(projectWorkingHoursEnd ?? orgWorkingHoursEnd);
        var lunch = projectLunchBreakMinutes ?? defaultLunchMinutes;

        // A non-advancing or malformed window tells us nothing, so fall back to
        // the standard day rather than paying OT off a zero-hour divisor.
        if (end <= start) return DefaultDailyHours;

        return Math.Max(0, end - start - lunch) / 60m;
    }

    // Combined OT pay. The per-category breakdown is persisted separately on the
    // payslip for audit; this is just the money.
    public static decimal Calculate(
        decimal hourlyRate,
        decimal otNormalHours,
        decimal otRestHours,
        decimal otPublicHours,
        decimal otRateNormal,
        decimal otRateRest,
        decimal otRatePublicHoliday)
    {
        var normal = otNormalHours * hourlyRate * otRateNormal;
        var rest = otRestHours * hourlyRate * otRateRest;
        var publicHoliday = otPublicHours * hourlyRate * otRatePublicHoliday;

        return Money.Round2(normal + rest + publicHoliday);
    }

    private const decimal DefaultDailyHours = 8m;

    // "HH:mm" → minutes past midnight. Anything unparseable reads as 0, which the
    // callers above treat as a malformed window.
    private static int MinutesFromHhmm(string value)
    {
        var parts = value.Split(':');
        if (parts.Length < 2) return 0;
        if (!int.TryParse(parts[0], out var hours)) return 0;
        if (!int.TryParse(parts[1], out var minutes)) return 0;

        return hours * 60 + minutes;
    }
}
