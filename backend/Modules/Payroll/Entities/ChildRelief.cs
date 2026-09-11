namespace AltomateHR.Api.Modules.Payroll.Entities;

public enum ChildAbilityStatus
{
    NORMAL,
    DISABLED,
}

// A child's studying level, for PCB relief under LHDN Public Ruling 5/2019 §7.3.
//
// The ruling only ever produces RM 2,000 or RM 8,000 per child, so the enum
// collapses to the four cases that map onto those two amounts.
//
//   UNDER_18         — under 18. RM 2,000, no further questions.
//   PRE_UNIVERSITY   — 18+, pre-uni / matriculation / A-levels / Form 6.
//                      RM 2,000 per §7.3.2.
//   DIPLOMA_MALAYSIA — 18+, diploma or above at an approved Malaysian
//                      institution. RM 8,000 per §7.3.3(a).
//   DEGREE_ABROAD    — 18+, degree or above at an approved institution
//                      outside Malaysia. RM 8,000 per §7.3.3(b).
//
// The last two carry the same relief; they are split so Form EA and CP8D can
// report the two cohorts separately.
public enum ChildStudyingLevel
{
    UNDER_18,
    PRE_UNIVERSITY,
    DIPLOMA_MALAYSIA,
    DEGREE_ABROAD,
}

// What share of a child's relief this employee claims. HALF is the 50/50 split
// when both parents claim the same child.
public enum ChildPcbDeductionLevel
{
    FULL,
    HALF,
    NONE,
}

// One child on the employee's relief declaration. Persisted inside
// `EmployeeProfile.ChildReliefJson` as an array.
public sealed record ChildRelief
{
    public ChildAbilityStatus AbilityStatus { get; init; } = ChildAbilityStatus.NORMAL;

    public ChildStudyingLevel CurrentlyStudying { get; init; } = ChildStudyingLevel.UNDER_18;

    // Defaults to NONE so a child added without an explicit claim contributes no
    // relief — under-claiming is recoverable at year end, over-claiming is not.
    public ChildPcbDeductionLevel PcbDeduction { get; init; } = ChildPcbDeductionLevel.NONE;
}
