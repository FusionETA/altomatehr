using AltomateHR.Api.Modules.Overtime;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

// Where a payslip's overtime hours come from.
//
// Approved overtime requests are the default, so OT an employee submitted and
// had approved is paid without the admin having to re-key it — v1 only
// pre-filled the hours when someone happened to open that employee's form,
// so approved OT could go unpaid simply because nobody looked.
//
// Anything the admin typed on the run's adjustment row wins outright: they
// may be correcting the approved figure, or paying OT that was agreed
// outside the request flow. "Typed" means any of the three is above zero —
// the same reading v1 used, because a saved zero is indistinguishable from
// "never set". The consequence: to pay LESS than the approved figure the
// admin types the lower number; to pay none of it, the request itself should
// be rejected, which is also what keeps the approval record honest.
public static class PayrollOvertimeHours
{
    public enum Source { None, Approved, Adjustment }

    // Custom: hours for the company's own overtime types, by type id.
    public readonly record struct Hours(
        decimal Normal, decimal Rest, decimal PublicHoliday, Source Source,
        IReadOnlyDictionary<string, decimal>? Custom = null)
    {
        public bool Any => Normal > 0m || Rest > 0m || PublicHoliday > 0m || (Custom?.Values.Any(h => h > 0m) ?? false);
    }

    public static Hours Resolve(PayrollRunAdjustment? adjustment, ApprovedOvertimeMinutes? approved)
    {
        // Any typed hours, built-in or custom, replace every approved hour.
        var typedCustom = adjustment is null
            ? new Dictionary<string, decimal>()
            : PayrollRunAdjustments.ParseOtCustomHours(adjustment.OtCustomHoursJson);
        if (adjustment is not null
            && (adjustment.OtNormalHours > 0m || adjustment.OtRestHours > 0m || adjustment.OtPublicHours > 0m
                || typedCustom.Values.Any(h => h > 0m)))
        {
            return new Hours(
                adjustment.OtNormalHours, adjustment.OtRestHours, adjustment.OtPublicHours,
                Source.Adjustment, typedCustom);
        }

        if (approved is null || approved.TotalMin <= 0)
            return new Hours(0m, 0m, 0m, Source.None);

        return new Hours(
            ToHours(approved.NormalDayMin),
            ToHours(approved.RestDayMin),
            ToHours(approved.PublicHolidayMin),
            Source.Approved,
            approved.CustomMin?.ToDictionary(c => c.Key, c => ToHours(c.Value), StringComparer.Ordinal));
    }

    // Rounded to 2dp — the precision the hours are stored and printed at — so
    // a payslip reads hours × rate × multiplier and lands on the figure paid.
    // The HOURLY RATE stays unrounded through the multiplication; that is the
    // rounding rule that matters for money, and it is the calculator's.
    public static decimal ToHours(int minutes) => Money.Round2(minutes / 60m);
}
