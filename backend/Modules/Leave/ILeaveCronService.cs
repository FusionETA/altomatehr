namespace AltomateHR.Api.Modules.Leave;

// Scheduled leave maintenance. Runs system-wide (every org) because it executes
// unauthenticated, outside any tenant context.
public interface ILeaveCronService
{
    // Runs on the 1st of each month. Two effects, mirroring production:
    //   1. PRO_RATED entitlements accrue EntitledDays/12, capped at EntitledDays.
    //   2. Carry-forward whose expiry has passed is swept and recorded.
    Task<AccrualResult> RunMonthlyAccrualAsync(DateTime now);

    // Runs at year end / new year. Opens `targetYear` by creating one entitlement
    // row per (employee × active type), carrying unused days forward where the
    // type allows it. Idempotent: rows that already exist are skipped.
    Task<RolloverResult> RunYearRolloverAsync(int targetYear, DateTime now);
}

public record AccrualResult(bool Ok, int AccruedCount, int ExpiredCount, int Year);
public record RolloverResult(bool Ok, int Created, int Skipped, int Year);
