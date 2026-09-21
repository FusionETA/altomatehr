using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

// Period arithmetic: how long the month is, and how much of it an employee was
// actually employed for.
//
// ⚠️ There are TWO divisors here and collapsing them into one is a statutory bug:
//
//   - s.60I ordinary rate of pay → `WorkingDaysForPeriod` (honours the org's
//     CALENDAR / TWENTY_SIX setting). Drives the hourly rate, hence overtime.
//   - s.18A incomplete month     → always CALENDAR days. Drives proration.
//
// s.18A opens "Notwithstanding section 60I", i.e. it deliberately overrides the
// ÷26 basis. Do not let the org setting leak into proration.
public static class PayPeriod
{
    public static int CalendarDaysInMonth(int year, int month) =>
        DateTime.DaysInMonth(year, month);

    // The s.60I basis: what the org counts as a month's worth of working days.
    public static int WorkingDaysForPeriod(int year, int month, WorkingDaysRule rule) =>
        rule == WorkingDaysRule.TWENTY_SIX ? 26 : CalendarDaysInMonth(year, month);

    // Mon-Sat days in the month (24-27). This is the six-day week the ÷26
    // convention assumes: 6 days × 52 weeks ÷ 12 ≈ 26, an AVERAGE no individual
    // month equals. Counting the real days keeps numerator and divisor on one
    // basis, which is what makes "days worked ÷ days in month" agree with
    // "salary − rate × days absent". A flat 26 makes them disagree: Jul 2026
    // has 27 Mon-Sat days, so a joiner who missed one would be paid 26/26 — a
    // full month; Feb 2026 has 24, so a joiner who missed nothing would be
    // docked RM 230.77 on a RM 3,000 salary.
    public static int SixDayWorkDaysInMonth(int year, int month)
    {
        var days = CalendarDaysInMonth(year, month);
        var count = 0;
        for (var day = 1; day <= days; day++)
        {
            if (new DateTime(year, month, day).DayOfWeek != DayOfWeek.Sunday) count++;
        }
        return count;
    }

    // Denominator for incomplete-month proration, per the org's rule.
    //
    //   CALENDAR   → calendar days in the month (EA s.18A as written)
    //   TWENTY_SIX → that month's real Mon-Sat count (24-27)
    //
    // Distinct from WorkingDaysForPeriod above, which stays on the flat 26
    // because it divides the HOURLY rate for overtime (EA s.60I). Two divisors,
    // two statutes — collapsing them would let an admin change everyone's
    // overtime rate by changing how joiners are prorated.
    public static int ProrationDaysForPeriod(int year, int month, WorkingDaysRule rule) =>
        rule == WorkingDaysRule.TWENTY_SIX
            ? SixDayWorkDaysInMonth(year, month)
            : CalendarDaysInMonth(year, month);

    // How many days of the period the employee is paid for, given their join and
    // leave dates.
    //
    // Null means they do not belong on this run at all — they joined after the
    // period ended, or left before it started.
    //
    // Employment Act s.18A prescribes the formula for an incomplete month:
    //
    //          monthly wages              number of days
    //   ─────────────────────────────  ×  eligible in the
    //   days of the wage period           wage period
    //
    // It covers all four cases below — joined after the 1st, left before month
    // end, unpaid leave, national service — so the count is CALENDAR days and the
    // divisor is the calendar days of the month. `daysInWagePeriod` must
    // therefore be the calendar length, never 26 and never a weekday roster.
    public static int? EffectiveWorkedDays(
        int periodYear,
        int periodMonth,
        DateTime? joinDate,
        DateTime? leaveDate,
        // Pass what ProrationDaysForPeriod returned for the same rule, never a
        // flat 26 — numerator and denominator must share one basis.
        int daysInWagePeriod,
        // CALENDAR counts every day in the eligible window; TWENTY_SIX counts
        // only Mon-Sat, matching the Mon-Sat denominator. Defaulted so existing
        // callers keep the s.18A behaviour.
        WorkingDaysRule rule = WorkingDaysRule.CALENDAR)
    {
        var periodStart = new DateTime(periodYear, periodMonth, 1);
        var periodEnd = new DateTime(
            periodYear, periodMonth, CalendarDaysInMonth(periodYear, periodMonth));

        var join = joinDate?.Date;
        var leave = leaveDate?.Date;

        if (join > periodEnd) return null;
        if (leave < periodStart) return null;

        var joinedBeforeThisPeriod = join is null || join <= periodStart;
        var stillHereAtPeriodEnd = leave is null || leave >= periodEnd;
        if (joinedBeforeThisPeriod && stillHereAtPeriodEnd) return daysInWagePeriod;

        var start = join > periodStart ? join!.Value : periodStart;
        var end = leave < periodEnd ? leave!.Value : periodEnd;

        // Inclusive of both endpoints — someone who joins and leaves on the same
        // day worked one day, not zero.
        var days = rule == WorkingDaysRule.TWENTY_SIX
            ? SixDayWorkDaysBetween(start, end)
            : (end - start).Days + 1;

        return Math.Clamp(days, 0, daysInWagePeriod);
    }

    // What to dock a MONTHLY employee for approved unpaid leave.
    //
    // The base salary is left whole and the absence comes off as its own line,
    // so the payslip reads "Basic 3,000 / Unpaid Leave −115.38" rather than a
    // silently smaller salary.
    //
    // The divisor is the s.60I ORDINARY-RATE basis — what one day of this
    // person's pay is worth — not s.18A proration. The two are different
    // statutes and this is the rate question, so the org's CALENDAR /
    // TWENTY_SIX setting legitimately applies here (unlike proration, where it
    // must not; see the note at the top of this file).
    //
    // Returns 0 when it does not apply: HOURLY staff are paid for the hours
    // they worked, so there is nothing to dock.
    public static decimal UnpaidLeaveDeduction(
        decimal? monthlySalary, decimal unpaidDays, int workingDaysBasis)
    {
        if (monthlySalary is null or <= 0m) return 0m;
        if (unpaidDays <= 0m || workingDaysBasis <= 0) return 0m;

        // Rounded once, at the end — the daily rate stays exact through the
        // multiplication.
        return Money.Round2(monthlySalary.Value / workingDaysBasis * unpaidDays);
    }

    // Mon-Sat days between two dates, both ends inclusive. The window is at
    // most one month, so a day-by-day walk is cheap and avoids weekday
    // arithmetic that goes wrong across month boundaries.
    private static int SixDayWorkDaysBetween(DateTime start, DateTime end)
    {
        var count = 0;
        for (var day = start.Date; day <= end.Date; day = day.AddDays(1))
        {
            if (day.DayOfWeek != DayOfWeek.Sunday) count++;
        }
        return count;
    }
}
