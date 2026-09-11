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
        int daysInWagePeriod)
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
        var days = (end - start).Days + 1;

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
}
