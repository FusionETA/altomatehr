using AltomateHR.Api.Modules.Leave.Entities;

namespace AltomateHR.Api.Modules.Leave;

// The leave arithmetic, kept pure and separate from data access so the rules are
// readable and directly testable. Ported from production's
// modules/leave/domain/accrual.ts.
public static class LeaveAccrualMath
{
    private const int MonthsPerYear = 12;

    // One month's accrual, never past the full entitlement.
    public static double NextAccruedDays(double entitledDays, double accruedDays) =>
        Math.Min(entitledDays, accruedDays + entitledDays / MonthsPerYear);

    // The bucket available for THIS year: everything up front for LUMP_SUM,
    // only what has accrued so far for PRO_RATED.
    public static double CurrentBucket(LeaveAccrualMethod method, double entitledDays, double accruedDays) =>
        method == LeaveAccrualMethod.PRO_RATED ? accruedDays : entitledDays;

    // Days the employee can apply for RIGHT NOW: this year's available bucket
    // plus any carry that hasn't lapsed, minus what they've taken.
    public static double AvailableDays(
        LeaveAccrualMethod method,
        double entitledDays,
        double accruedDays,
        double carriedDays,
        bool carriedExpired,
        double usedDays)
    {
        var carry = carriedExpired ? 0 : carriedDays;
        return Math.Max(0, CurrentBucket(method, entitledDays, accruedDays) + carry - usedDays);
    }

    // How much rolls into next year, capped by the type's ceiling.
    public static double CarryForwardAmount(
        LeaveAccrualMethod method,
        double entitledDays,
        double accruedDays,
        double carriedDays,
        double usedDays,
        double? maxCarryForwardDays)
    {
        var remaining = Math.Max(0, CurrentBucket(method, entitledDays, accruedDays) + carriedDays - usedDays);
        return maxCarryForwardDays is null
            ? remaining
            : Math.Min(remaining, Math.Max(0, maxCarryForwardDays.Value));
    }

    // Carried days not yet consumed at expiry time. The current-year bucket is
    // assumed to be spent FIRST — otherwise an employee would always be racing
    // the clock against their carried days.
    public static double UnusedCarriedAtExpiry(
        LeaveAccrualMethod method,
        double entitledDays,
        double accruedDays,
        double carriedDays,
        double usedDays)
    {
        var usedFromCarry = Math.Max(0, usedDays - CurrentBucket(method, entitledDays, accruedDays));
        return Math.Max(0, carriedDays - usedFromCarry);
    }
}
