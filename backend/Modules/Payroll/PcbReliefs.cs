using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

// The compulsory personal and family reliefs, per LHDN PCB Specification 2026
// Section E (pages 26–29):
//
//   D  — individual                RM 9,000  (every resident)
//   DU — disabled individual       RM 7,000  (on top of D)
//   S  — spouse                    RM 4,000  (only when the spouse has no income)
//   SU — disabled spouse           RM 6,000  (on top of S)
//   QC — per child                 see ForChild
//
// EPF and PERKESO reliefs are NOT here — they depend on the period's actual
// contributions, so PcbCalculator adds them.
public static class PcbReliefs
{
    private const decimal Individual = 9000m;
    private const decimal DisabledIndividual = 7000m;
    private const decimal Spouse = 4000m;
    private const decimal DisabledSpouse = 6000m;

    // Annual EPF employee contribution relief, capped per year of assessment.
    public const decimal EpfCap = 4000m;

    // Combined PERKESO relief (SOCSO + EIS + SKBBK employee shares), capped per
    // year of assessment. SKBBK is a PERKESO scheme and shares this bucket.
    //
    // Strictly this is a TP1 (employee-declared) deduction. We auto-apply it, as
    // HReasily / BrioHR / Talenox do, because the employer already deducts the
    // amount and knows it exactly — there is no information asymmetry to justify
    // a declaration step. The over-claim risk is nil: someone at both wage
    // ceilings contributes ~RM 98.75/month, so the cap is reached by mid-year
    // regardless. TP1 items the employer cannot know — life insurance,
    // lifestyle, parents' medical — stay employee-declared.
    public const decimal PerkesoCap = 350m;

    public sealed record Breakdown
    {
        public decimal Individual { get; init; }
        public decimal DisabledIndividual { get; init; }
        public decimal Spouse { get; init; }
        public decimal DisabledSpouse { get; init; }

        // Per-child amounts, in the order the children were declared.
        public IReadOnlyList<decimal> ChildItems { get; init; } = [];

        public decimal Children { get; init; }
        public decimal Total { get; init; }
    }

    // Relief for one child. LHDN expresses the larger amounts as multiples of
    // the RM 2,000 base — 4 children's worth for either disability or higher
    // education, 8 for both.
    public static decimal ForChild(ChildRelief child)
    {
        if (child.PcbDeduction == ChildPcbDeductionLevel.NONE) return 0m;

        var isDisabled = child.AbilityStatus == ChildAbilityStatus.DISABLED;

        // "Higher education" is diploma-or-above at an approved institution.
        // Malaysia and abroad carry the same amount; they differ only in how the
        // annual Form EA reports them.
        var isHigherEd = child.CurrentlyStudying
            is ChildStudyingLevel.DIPLOMA_MALAYSIA
            or ChildStudyingLevel.DEGREE_ABROAD;

        var total = (isDisabled, isHigherEd) switch
        {
            (true, true) => 16000m,    // 8 × RM 2,000
            (true, false) => 8000m,    // 4 × RM 2,000
            (false, true) => 8000m,    // 4 × RM 2,000
            _ => 2000m,                // UNDER_18 or PRE_UNIVERSITY
        };

        // HALF is the 50/50 split when both parents claim the same child.
        return child.PcbDeduction == ChildPcbDeductionLevel.HALF
            ? Money.Round2(total / 2m)
            : total;
    }

    public static decimal Total(
        bool isOku,
        bool? spouseWorking,
        bool? spouseDisabled,
        IReadOnlyList<ChildRelief> children) =>
        Itemise(isOku, spouseWorking, spouseDisabled, children).Total;

    // The itemised view — the admin UI previews this before a run.
    public static Breakdown Itemise(
        bool isOku,
        bool? spouseWorking,
        bool? spouseDisabled,
        IReadOnlyList<ChildRelief> children)
    {
        // Only a definite "spouse does not work" opens the S relief. Unknown
        // (null) does not — the same gate governs the RM 400 spouse rebate.
        var spouseClaimable = spouseWorking == false;

        var disabledIndividual = isOku ? DisabledIndividual : 0m;
        var spouse = spouseClaimable ? Spouse : 0m;
        var disabledSpouse = spouseClaimable && spouseDisabled == true ? DisabledSpouse : 0m;

        var childItems = children.Select(ForChild).ToList();
        var childTotal = childItems.Sum();

        return new Breakdown
        {
            Individual = Individual,
            DisabledIndividual = disabledIndividual,
            Spouse = spouse,
            DisabledSpouse = disabledSpouse,
            ChildItems = childItems,
            Children = childTotal,
            Total = Individual + disabledIndividual + spouse + disabledSpouse + childTotal,
        };
    }
}
