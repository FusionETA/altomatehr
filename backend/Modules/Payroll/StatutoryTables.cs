using AltomateHR.Api.Modules.Employees.Entities;

namespace AltomateHR.Api.Modules.Payroll;

// Statutory contribution tables — the official Third Schedule lookups.
//
// SOURCES (gazetted PDFs, verified row-by-row):
//   - SOCSO  Act 4   "Rate of contribution for Employees Social Security Act 1969"
//   - EIS    Act 800 "Rate of Contribution Employment Insurance System"
//   - SKBBK          PERKESO Pekeliling Majikan Bil. 02-2026
//   - EPF            KWSP Third Schedule (Parts A / C / E / F)
//
// Each table is sorted by `UpTo`; the lookup is "first row where wage <= UpTo".
// The ceiling row uses `decimal.MaxValue` and matches anything above the cap.
//
// ⚠️ Never change a rate here without citing the circular that changed it.
// These are gazette values, not tuning knobs.
public static class StatutoryTables
{
    // Inclusive upper wage bound (RM) → the monthly contribution each side pays.
    public readonly record struct StatutoryRow(decimal UpTo, decimal Employer, decimal Employee);

    // SOCSO carries two extra columns:
    //   Employer2 — Cat-2 (Employment Injury Only) employer share; employee pays 0.
    //   Skbbk     — Skim LINDUNG 24 Jam employee-only share, effective 1 Jun 2026.
    //               PERKESO publishes ONE SKBBK column that both categories read off.
    public readonly record struct SocsoRow(
        decimal UpTo, decimal Employer, decimal Employee, decimal Employer2, decimal Skbbk);

    // ─── SOCSO Act 4 Third Schedule (65 rows) ───────────────────────────

    public static readonly IReadOnlyList<SocsoRow> SocsoTable =
    [
    new(30m, 0.4m, 0.1m, 0.3m, 0.2m),
    new(50m, 0.7m, 0.2m, 0.5m, 0.3m),
    new(70m, 1.1m, 0.3m, 0.8m, 0.5m),
    new(100m, 1.5m, 0.4m, 1.1m, 0.65m),
    new(140m, 2.1m, 0.6m, 1.5m, 0.9m),
    new(200m, 2.95m, 0.85m, 2.1m, 1.25m),
    new(300m, 4.35m, 1.25m, 3.1m, 1.85m),
    new(400m, 6.15m, 1.75m, 4.4m, 2.65m),
    new(500m, 7.85m, 2.25m, 5.6m, 3.35m),
    new(600m, 9.65m, 2.75m, 6.9m, 4.15m),
    new(700m, 11.35m, 3.25m, 8.1m, 4.85m),
    new(800m, 13.15m, 3.75m, 9.4m, 5.65m),
    new(900m, 14.85m, 4.25m, 10.6m, 6.35m),
    new(1000m, 16.65m, 4.75m, 11.9m, 7.15m),
    new(1100m, 18.35m, 5.25m, 13.1m, 7.85m),
    new(1200m, 20.15m, 5.75m, 14.4m, 8.65m),
    new(1300m, 21.85m, 6.25m, 15.6m, 9.35m),
    new(1400m, 23.65m, 6.75m, 16.9m, 10.15m),
    new(1500m, 25.35m, 7.25m, 18.1m, 10.85m),
    new(1600m, 27.15m, 7.75m, 19.4m, 11.65m),
    new(1700m, 28.85m, 8.25m, 20.6m, 12.35m),
    new(1800m, 30.65m, 8.75m, 21.9m, 13.15m),
    new(1900m, 32.35m, 9.25m, 23.1m, 13.85m),
    new(2000m, 34.15m, 9.75m, 24.4m, 14.65m),
    new(2100m, 35.85m, 10.25m, 25.6m, 15.35m),
    new(2200m, 37.65m, 10.75m, 26.9m, 16.15m),
    new(2300m, 39.35m, 11.25m, 28.1m, 16.85m),
    new(2400m, 41.15m, 11.75m, 29.4m, 17.65m),
    new(2500m, 42.85m, 12.25m, 30.6m, 18.35m),
    new(2600m, 44.65m, 12.75m, 31.9m, 19.15m),
    new(2700m, 46.35m, 13.25m, 33.1m, 19.85m),
    new(2800m, 48.15m, 13.75m, 34.4m, 20.65m),
    new(2900m, 49.85m, 14.25m, 35.6m, 21.35m),
    new(3000m, 51.65m, 14.75m, 36.9m, 22.15m),
    new(3100m, 53.35m, 15.25m, 38.1m, 22.85m),
    new(3200m, 55.15m, 15.75m, 39.4m, 23.65m),
    new(3300m, 56.85m, 16.25m, 40.6m, 24.35m),
    new(3400m, 58.65m, 16.75m, 41.9m, 25.15m),
    new(3500m, 60.35m, 17.25m, 43.1m, 25.85m),
    new(3600m, 62.15m, 17.75m, 44.4m, 26.65m),
    new(3700m, 63.85m, 18.25m, 45.6m, 27.35m),
    new(3800m, 65.65m, 18.75m, 46.9m, 28.15m),
    new(3900m, 67.35m, 19.25m, 48.1m, 28.85m),
    new(4000m, 69.15m, 19.75m, 49.4m, 29.65m),
    new(4100m, 70.85m, 20.25m, 50.6m, 30.35m),
    new(4200m, 72.65m, 20.75m, 51.9m, 31.15m),
    new(4300m, 74.35m, 21.25m, 53.1m, 31.85m),
    new(4400m, 76.15m, 21.75m, 54.4m, 32.65m),
    new(4500m, 77.85m, 22.25m, 55.6m, 33.35m),
    new(4600m, 79.65m, 22.75m, 56.9m, 34.15m),
    new(4700m, 81.35m, 23.25m, 58.1m, 34.85m),
    new(4800m, 83.15m, 23.75m, 59.4m, 35.65m),
    new(4900m, 84.85m, 24.25m, 60.6m, 36.35m),
    new(5000m, 86.65m, 24.75m, 61.9m, 37.15m),
    new(5100m, 88.35m, 25.25m, 63.1m, 37.85m),
    new(5200m, 90.15m, 25.75m, 64.4m, 38.65m),
    new(5300m, 91.85m, 26.25m, 65.6m, 39.35m),
    new(5400m, 93.65m, 26.75m, 66.9m, 40.15m),
    new(5500m, 95.35m, 27.25m, 68.1m, 40.85m),
    new(5600m, 97.15m, 27.75m, 69.4m, 41.65m),
    new(5700m, 98.85m, 28.25m, 70.6m, 42.35m),
    new(5800m, 100.65m, 28.75m, 71.9m, 43.15m),
    new(5900m, 102.35m, 29.25m, 73.1m, 43.85m),
    new(6000m, 104.15m, 29.75m, 74.4m, 44.65m),
    new(decimal.MaxValue, 104.15m, 29.75m, 74.4m, 44.65m),
    ];

    // ─── EIS Act 800 Third Schedule (65 rows) ───────────────────────────

    public static readonly IReadOnlyList<StatutoryRow> EisTable =
    [
    new(30m, 0.05m, 0.05m),
    new(50m, 0.1m, 0.1m),
    new(70m, 0.15m, 0.15m),
    new(100m, 0.2m, 0.2m),
    new(140m, 0.25m, 0.25m),
    new(200m, 0.35m, 0.35m),
    new(300m, 0.5m, 0.5m),
    new(400m, 0.7m, 0.7m),
    new(500m, 0.9m, 0.9m),
    new(600m, 1.1m, 1.1m),
    new(700m, 1.3m, 1.3m),
    new(800m, 1.5m, 1.5m),
    new(900m, 1.7m, 1.7m),
    new(1000m, 1.9m, 1.9m),
    new(1100m, 2.1m, 2.1m),
    new(1200m, 2.3m, 2.3m),
    new(1300m, 2.5m, 2.5m),
    new(1400m, 2.7m, 2.7m),
    new(1500m, 2.9m, 2.9m),
    new(1600m, 3.1m, 3.1m),
    new(1700m, 3.3m, 3.3m),
    new(1800m, 3.5m, 3.5m),
    new(1900m, 3.7m, 3.7m),
    new(2000m, 3.9m, 3.9m),
    new(2100m, 4.1m, 4.1m),
    new(2200m, 4.3m, 4.3m),
    new(2300m, 4.5m, 4.5m),
    new(2400m, 4.7m, 4.7m),
    new(2500m, 4.9m, 4.9m),
    new(2600m, 5.1m, 5.1m),
    new(2700m, 5.3m, 5.3m),
    new(2800m, 5.5m, 5.5m),
    new(2900m, 5.7m, 5.7m),
    new(3000m, 5.9m, 5.9m),
    new(3100m, 6.1m, 6.1m),
    new(3200m, 6.3m, 6.3m),
    new(3300m, 6.5m, 6.5m),
    new(3400m, 6.7m, 6.7m),
    new(3500m, 6.9m, 6.9m),
    new(3600m, 7.1m, 7.1m),
    new(3700m, 7.3m, 7.3m),
    new(3800m, 7.5m, 7.5m),
    new(3900m, 7.7m, 7.7m),
    new(4000m, 7.9m, 7.9m),
    new(4100m, 8.1m, 8.1m),
    new(4200m, 8.3m, 8.3m),
    new(4300m, 8.5m, 8.5m),
    new(4400m, 8.7m, 8.7m),
    new(4500m, 8.9m, 8.9m),
    new(4600m, 9.1m, 9.1m),
    new(4700m, 9.3m, 9.3m),
    new(4800m, 9.5m, 9.5m),
    new(4900m, 9.7m, 9.7m),
    new(5000m, 9.9m, 9.9m),
    new(5100m, 10.1m, 10.1m),
    new(5200m, 10.3m, 10.3m),
    new(5300m, 10.5m, 10.5m),
    new(5400m, 10.7m, 10.7m),
    new(5500m, 10.9m, 10.9m),
    new(5600m, 11.1m, 11.1m),
    new(5700m, 11.3m, 11.3m),
    new(5800m, 11.5m, 11.5m),
    new(5900m, 11.7m, 11.7m),
    new(6000m, 11.9m, 11.9m),
    new(decimal.MaxValue, 11.9m, 11.9m),
    ];

    // Wage at or below this earns no EPF at all (KWSP de-minimis).
    private const decimal EpfDeMinimisWage = 10m;

    // Both statutory schemes stop contributing above this monthly wage.
    public const decimal SocsoEisWageCeiling = 6000m;

    // ─── Lookups ────────────────────────────────────────────────────────

    // Cat 2 (Employment Injury Only) pays an employer share only.
    public static (decimal Employer, decimal Employee) LookupSocso(decimal wage, bool category2)
    {
        if (wage <= 0) return (0m, 0m);

        foreach (var row in SocsoTable)
        {
            if (wage > row.UpTo) continue;
            return category2 ? (row.Employer2, 0m) : (row.Employer, row.Employee);
        }

        // Unreachable — the last row's UpTo is decimal.MaxValue — but a wage
        // lookup should never throw, so fall back to the ceiling band.
        var last = SocsoTable[^1];
        return category2 ? (last.Employer2, 0m) : (last.Employer, last.Employee);
    }

    public static (decimal Employer, decimal Employee) LookupEis(decimal wage)
    {
        if (wage <= 0) return (0m, 0m);

        foreach (var row in EisTable)
        {
            if (wage <= row.UpTo) return (row.Employer, row.Employee);
        }

        var last = EisTable[^1];
        return (last.Employer, last.Employee);
    }

    // ─── KWSP Third Schedule — rule-based, not tabulated ────────────────
    //
    // Parts A and C of the gazetted schedule are 401 rows each, but they follow
    // a fully-specified rule, so we encode the rule instead of the rows:
    //
    //   Wage  0.01 ..     10 → NIL (de-minimis)
    //   Wage 10.01 ..  5,000 → RM 20 wage bands;  each side = ceil(rate × bandUpperBound)
    //   Wage  5,000.01 .. 20,000 → RM 100 wage bands; employer rate drops 1pp at the cliff
    //   Wage      > 20,000 → exact percentage, each side rounded UP to the next ringgit
    //                        (Third Schedule Note 2)
    //
    // Rate inputs per branch:
    //   Part A (Malaysian / PR / pre-1998, under 60): employer 13 → 12, employee 11
    //   Part C (PR / pre-1998, 60+):                  employer 6.5 → 6, employee 5.5
    //   Part E (Malaysian citizen 60+):               employer 4 flat,  employee 0
    //   Part F (post-1998 non-Malaysian, any age):    employer 2 flat,  employee 2
    //
    // Parts E and F are gazetted as flat percentages with NO band table, so the
    // off-table rule applies to them at every wage. We detect that by the absence
    // of a rate cliff (employerRateLow == employerRateHigh) rather than by naming
    // the part — banding a flat-rate branch overstates the contribution by up to
    // RM 1, which is a real defect the reference app hit in production.
    public static (decimal Employer, decimal Employee) LookupEpfBand(
        decimal wage,
        decimal employerRateLow,
        decimal employerRateHigh,
        decimal employeeRate,
        // Which wage picks the employer rate at the RM 5,000 cliff. Defaults to
        // `wage`, the strict gazetted reading where TOTAL monthly wages decide.
        // Callers wanting the looser "cliff by contractual monthly wage" reading —
        // so a one-off bonus doesn't push an under-RM-5,000 employee up a tier —
        // pass the regular-monthly portion here while leaving `wage` at the full
        // combined amount.
        decimal? rateDeterminingWage = null)
    {
        if (wage <= EpfDeMinimisWage) return (0m, 0m);

        var rateWage = rateDeterminingWage ?? wage;
        var hasCliff = employerRateLow != employerRateHigh;

        // Flat-rate branch (Parts E / F): exact percentage, rounded up each side.
        if (!hasCliff) return (CeilRinggit(wage, employerRateHigh), CeilRinggit(wage, employeeRate));

        decimal bandUpper;
        decimal employerRate;

        if (rateWage <= 5000m)
        {
            bandUpper = Math.Ceiling(wage / 20m) * 20m;
            employerRate = employerRateLow;
        }
        else if (rateWage <= 20000m)
        {
            bandUpper = Math.Ceiling(wage / 100m) * 100m;
            employerRate = employerRateHigh;
        }
        else
        {
            // Above the table cap — off-table rule.
            return (CeilRinggit(wage, employerRateHigh), CeilRinggit(wage, employeeRate));
        }

        return (CeilRinggit(bandUpper, employerRate), CeilRinggit(bandUpper, employeeRate));
    }

    // A percentage of a wage, rounded UP to the next whole ringgit.
    private static decimal CeilRinggit(decimal wage, decimal ratePercent) =>
        Math.Ceiling(wage * ratePercent / 100m);

    // ─── SKBBK / Skim LINDUNG 24 Jam ────────────────────────────────────
    //
    // PERKESO's Non-Employment Injury Security Scheme — extends SOCSO with
    // 24-hour cover for non-work accidents. Effective 1 Jun 2026.
    //
    //   - Employee-only contribution; no employer share.
    //   - Phased rollout 0.75% → 1.0% → 1.25%; phase 2/3 dates not yet gazetted.
    //   - Capped at RM 6,000/month, same as SOCSO.
    //   - Applies to ALL Akta 4 employees (local, PR, foreign; any age).
    //
    // The amounts are gazette-verbatim, NOT `rate × wage` — band 5 pays RM 0.90
    // where 0.75% × 140 would give RM 1.05. Read the table, never the formula.

    // A phase boundary: periods on or after (StartYear, StartMonth) — and before
    // the next entry — use this phase. `EmployeeRatePct` is display-only; the
    // money always comes from `Amounts`.
    public sealed record SkbbkPhase(
        int StartYear, int StartMonth, decimal EmployeeRatePct, IReadOnlyList<decimal> Amounts);

    // Indexed in lockstep with SocsoTable, so the wage bounds are never duplicated.
    private static readonly IReadOnlyList<decimal> SkbbkPhase1Amounts =
        [.. SocsoTable.Select(r => r.Skbbk)];

    // Descending by start date — first match wins. Keep sorted when adding a phase.
    public static readonly IReadOnlyList<SkbbkPhase> SkbbkPhaseSchedule =
    [
        new(StartYear: 2026, StartMonth: 6, EmployeeRatePct: 0.75m, Amounts: SkbbkPhase1Amounts),
    ];

    // Null for periods before 1 Jun 2026 — SKBBK did not exist, so a historical
    // run recomputing itself must produce 0, not today's rate.
    public static SkbbkPhase? GetSkbbkPhaseForPeriod(int periodYear, int periodMonth)
    {
        var periodOrdinal = periodYear * 12 + periodMonth;

        foreach (var phase in SkbbkPhaseSchedule)
        {
            if (periodOrdinal >= phase.StartYear * 12 + phase.StartMonth) return phase;
        }

        return null;
    }

    public static decimal LookupSkbbk(decimal wage, int periodYear, int periodMonth)
    {
        var phase = GetSkbbkPhaseForPeriod(periodYear, periodMonth);
        if (phase is null) return 0m;
        if (wage <= EpfDeMinimisWage) return 0m;

        for (var i = 0; i < SocsoTable.Count; i++)
        {
            if (wage <= SocsoTable[i].UpTo) return phase.Amounts[i];
        }

        return phase.Amounts[^1];
    }
}
