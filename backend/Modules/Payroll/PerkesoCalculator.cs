using AltomateHR.Api.Modules.Employees.Entities;

namespace AltomateHR.Api.Modules.Payroll;

// The two PERKESO-administered contributions, plus SKBBK.
//
// SOCSO — Employees' Social Security Act 1969 (Act 4), Third Schedule:
//   Cat 1 (Employment Injury + Invalidity, under 60) — both sides contribute.
//   Cat 2 (Employment Injury only, 60+)              — employer only.
//
// EIS — Employment Insurance System Act 2017 (Act 800), Third Schedule.
//
// Both are 65-row stepped tables capped at RM 6,000, NOT percentages. Always
// read the table.
public static class PerkesoCalculator
{
    public readonly record struct SocsoResult(
        decimal Employee, decimal Employer, decimal EmployeeSkbbk);

    public readonly record struct EisResult(decimal Employee, decimal Employer);

    public sealed record SocsoInput
    {
        public required decimal Wage { get; init; }

        // Null means the employee is outside SOCSO scope entirely.
        public required SocsoScheme? Scheme { get; init; }

        // SKBBK only exists from Jun 2026 and is gazetted in phases, so the
        // period — not today's date — decides which table applies. A rerun of an
        // old period must reproduce that period's numbers.
        public required int PeriodYear { get; init; }
        public required int PeriodMonth { get; init; }

        // Age at the END of the period. Null keeps the stored scheme as-is.
        public int? AgeAtPeriodEnd { get; init; }

        // Per-employee SKBBK opt-in. Required with no default on purpose: a
        // statutory contribution must never switch on or off because a caller
        // forgot to pass it.
        public required bool ContributeToSkbbk { get; init; }
    }

    public static SocsoResult CalculateSocso(SocsoInput input)
    {
        if (input.Scheme is null) return new SocsoResult(0m, 0m, 0m);

        // Mandatory age flip: at 60 the employee moves to Category 2 whatever the
        // profile says, because Invalidity cover ends at retirement age. Mirrors
        // the Part E flip in EpfCalculator. Without it, an admin who forgets to
        // update the profile keeps deducting employee SOCSO from a 60+ worker,
        // and PERKESO refunds it at year end anyway.
        var effectiveScheme = input.AgeAtPeriodEnd >= 60
            ? SocsoScheme.EMPLOYMENT_INJURY_ONLY
            : input.Scheme.Value;

        var category2 = effectiveScheme == SocsoScheme.EMPLOYMENT_INJURY_ONLY;
        var (employer, employee) = StatutoryTables.LookupSocso(input.Wage, category2);

        // One gazette column serves both categories. The period gate inside
        // LookupSkbbk returns 0 before Jun 2026, so a historical rerun can't
        // back-bill a contribution that didn't exist yet.
        var skbbk = input.ContributeToSkbbk
            ? StatutoryTables.LookupSkbbk(input.Wage, input.PeriodYear, input.PeriodMonth)
            : 0m;

        return new SocsoResult(employee, employer, skbbk);
    }

    // Eligibility (citizen / PR / temporary resident, age 18–60) is gated by the
    // orchestrator before this is reached; here `contributeToEis` is the answer.
    public static EisResult CalculateEis(decimal wage, bool contributeToEis)
    {
        if (!contributeToEis) return new EisResult(0m, 0m);

        var (employer, employee) = StatutoryTables.LookupEis(wage);
        return new EisResult(employee, employer);
    }
}
