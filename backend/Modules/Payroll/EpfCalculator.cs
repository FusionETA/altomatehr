namespace AltomateHR.Api.Modules.Payroll;

// Which KWSP Third Schedule branch an employee falls into for one period. The
// branch fixes the MANDATORY rate; voluntary contributions stack on top of every
// branch.
public enum EpfBranch
{
    // Part A — citizen / PR / pre-1998 non-Malaysian, under 60.
    MALAYSIAN_UNDER_60,

    // Part E — Malaysian citizen at 60+. Employer only.
    MALAYSIAN_CITIZEN_60_PLUS,

    // Part C — PR or pre-1998 non-Malaysian at 60+.
    PR_OR_PRE1998_60_PLUS,

    // Part F — non-Malaysian who registered on or after 1 Aug 1998. Any age.
    POST_1998_NON_MALAYSIAN,

    // Wage at or below RM 10 — no contribution at all.
    DE_MINIMIS,

    // The employee is not an EPF contributor.
    OPTED_OUT,
}

// EPF (Employees Provident Fund) — KWSP Third Schedule Parts A / C / E / F.
//
//   Part A (< 60)                      employer 13% (≤RM5k) / 12%, employee 11%
//   Part C (PR or pre-1998 non-MY, 60+) employer 6.5% / 6%,        employee 5.5%
//   Part E (Malaysian citizen, 60+)     employer 4% flat,          employee 0%
//   Part F (post-1998 non-MY, any age)  employer 2% flat,          employee 2%
//
// Source: KWSP Contribution Rate table (Third Schedule). The Oct-2025 amendment
// introduced Part F, superseding the old Parts B / D.
public static class EpfCalculator
{
    // Everything the branch decision and the money need. Split out so the
    // orchestrator can hand over plain values with no entity coupling.
    public sealed record Input
    {
        // Regular monthly EPF-able wage: base salary + recurring allowances.
        // This drives the gazetted band lookup. One-off bonus / commission /
        // arrears do NOT belong here — see AdditionalRemuneration.
        public required decimal Wage { get; init; }

        // EPF-able Additional Remuneration — one-off amounts that must not push
        // the employee across the RM 5,000 employer-rate cliff on their own.
        public decimal AdditionalRemuneration { get; init; }

        // Which wage decides the cliff. Defaults to Wage. The orchestrator passes
        // the regular monthly portion so a bonus month doesn't move an
        // under-RM-5,000 employee onto the 12% tier for one run.
        public decimal? RateDeterminingWage { get; init; }

        // The admin's declared employee rate. Only Part A honours it, and only
        // upward — see the clamp in Calculate.
        public required decimal EmployeeRate { get; init; }

        public decimal EmployeeVoluntary { get; init; }
        public decimal EmployerVoluntary { get; init; }

        public required bool ContributeToEpf { get; init; }
        public required bool IsMalaysianCitizen { get; init; }
        public required bool HasPr { get; init; }

        // Non-Malaysian who was already an EPF member before 1 Aug 1998 — keeps
        // them on Parts A / C instead of dropping to Part F.
        public required bool EpfMemberBefore1998 { get; init; }

        // Age at the END of the period. 60+ flips into Part C or E.
        public required int AgeAtPeriodEnd { get; init; }
    }

    public readonly record struct Result(decimal Employee, decimal Employer, EpfBranch Branch);

    // Wage at or below this earns nothing.
    private const decimal DeMinimisWage = 10m;

    // The employer-rate cliff, and the top of the gazetted band table.
    private const decimal RateCliffWage = 5000m;
    private const decimal BandTableCeiling = 20000m;

    // Statutory minimum employee share under Part A. The COVID-era 9% election
    // has ended and KWSP form 17A only lets an employee contribute ABOVE the
    // statutory rate, so we clamp upward — anything extra belongs in
    // EmployeeVoluntary, not in a reduced base rate.
    private const decimal PartAMinimumEmployeeRate = 11m;

    public static EpfBranch PickBranch(
        bool contributeToEpf,
        decimal wage,
        bool isMalaysianCitizen,
        bool hasPr,
        bool epfMemberBefore1998,
        int ageAtPeriodEnd)
    {
        if (!contributeToEpf) return EpfBranch.OPTED_OUT;
        if (wage <= DeMinimisWage) return EpfBranch.DE_MINIMIS;

        var eligibleForPartsAOrC = isMalaysianCitizen || hasPr || epfMemberBefore1998;

        // Post-1998 non-Malaysian, not PR — same flat rate at any age, any wage.
        if (!eligibleForPartsAOrC) return EpfBranch.POST_1998_NON_MALAYSIAN;

        if (ageAtPeriodEnd < 60) return EpfBranch.MALAYSIAN_UNDER_60;

        // At 60+, citizens drop to Part E; PR and pre-1998 non-Malaysians stay on
        // the tiered Part C.
        //
        // `hasPr` deliberately plays no part in the citizen test. A Malaysian
        // citizen is permanent in Malaysia by definition, and the profile UI
        // auto-locks hasPr=true for them — testing `isMalaysianCitizen && !hasPr`
        // routed citizens into Part C and over-collected 5.5% employee EPF from
        // people legally entitled to Part E's 0%.
        return isMalaysianCitizen
            ? EpfBranch.MALAYSIAN_CITIZEN_60_PLUS
            : EpfBranch.PR_OR_PRE1998_60_PLUS;
    }

    public static Result Calculate(Input input)
    {
        var branch = PickBranch(
            input.ContributeToEpf,
            input.Wage,
            input.IsMalaysianCitizen,
            input.HasPr,
            input.EpfMemberBefore1998,
            input.AgeAtPeriodEnd);

        if (branch is EpfBranch.OPTED_OUT or EpfBranch.DE_MINIMIS)
        {
            return new Result(0m, 0m, branch);
        }

        var (employerRateLow, employerRateHigh, employeeRate) = branch switch
        {
            EpfBranch.MALAYSIAN_UNDER_60 =>
                (13m, 12m, Math.Max(PartAMinimumEmployeeRate, input.EmployeeRate)),
            EpfBranch.MALAYSIAN_CITIZEN_60_PLUS => (4m, 4m, 0m),
            EpfBranch.PR_OR_PRE1998_60_PLUS => (6.5m, 6m, 5.5m),
            EpfBranch.POST_1998_NON_MALAYSIAN => (2m, 2m, 2m),
            _ => throw new InvalidOperationException($"Unhandled EPF branch {branch}."),
        };

        var totalWage = input.Wage + input.AdditionalRemuneration;
        var rateWage = input.RateDeterminingWage ?? input.Wage;

        var employerMandatoryRate = rateWage <= RateCliffWage ? employerRateLow : employerRateHigh;
        var employeeVoluntaryRate = Math.Max(0m, input.EmployeeVoluntary);
        var employerVoluntaryRate = Math.Max(0m, input.EmployerVoluntary);

        // Two paths, per KWSP Third Schedule Note 2:
        //
        //   1. Band table — Parts A and C (the branches with a cliff) at wages up
        //      to RM 20,000. KWSP MANDATES the gazetted stepped table for the
        //      mandatory portion of BOTH sides. Voluntary has no place in the
        //      Schedule, so it is added as a separate ceil on top.
        //
        //   2. Off table — the flat-rate branches (E and F) at any wage, and any
        //      branch above RM 20,000. Exact percentage, rounded up ONCE PER SIDE.
        //      Ceiling mandatory and voluntary separately over-collects by up to
        //      RM 1 per side, which is the bug this shape exists to avoid.
        var hasCliff = employerRateLow != employerRateHigh;
        var usesBandTable = hasCliff && totalWage <= BandTableCeiling;

        decimal employee;
        decimal employer;

        if (usesBandTable)
        {
            var band = StatutoryTables.LookupEpfBand(
                totalWage, employerRateLow, employerRateHigh, employeeRate);

            employee = band.Employee + Money.CeilRinggit(totalWage, employeeVoluntaryRate);

            // Whether AR lifted the wage decides the employer treatment. Callers
            // pass the whole EPF-subject bonus inside Wage and signal AR only by
            // supplying a lower RateDeterminingWage, so compare the two:
            //
            //   no lift  → band table on the total, mirroring the employee side.
            //   AR lift  → single ceil of (mandatory + voluntary) × total, with
            //              the mandatory rate still chosen by the regular wage,
            //              so a bonus can't tip a sub-RM-5,000 employee to 12%.
            var hasArLift = rateWage < totalWage;

            employer = hasArLift
                ? Money.CeilRinggit(totalWage, employerMandatoryRate + employerVoluntaryRate)
                : band.Employer + Money.CeilRinggit(totalWage, employerVoluntaryRate);
        }
        else
        {
            employee = Money.CeilRinggit(totalWage, employeeRate + employeeVoluntaryRate);
            employer = Money.CeilRinggit(totalWage, employerMandatoryRate + employerVoluntaryRate);
        }

        return new Result(Money.Round2(employee), Money.Round2(employer), branch);
    }
}
