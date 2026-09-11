using AltomateHR.Api.Modules.Employees.Entities;

namespace AltomateHR.Api.Modules.Payroll;

// Which SOCSO scheme an employee belongs to, per PERKESO's classification rules.
//
// This is advice, not a rate: it fills the dropdown on the employee's statutory
// tab. The stored `EmployeeProfile.SocsoScheme` is what payroll actually reads —
// an admin can always override what we recommend here.
//
// Malaysian citizens:
//   age < 55   → Scheme 1 (Injury + Invalidity)
//   age 55–59  → ambiguous: depends on whether they are a first-time SOCSO
//                registrant, which we cannot detect. Return null and ask.
//   age ≥ 60   → Scheme 2 (Employment Injury only)
//
// Non-Malaysians (post Oct-2025 PERKESO expansion — foreign workers now fall
// under SOCSO the same way citizens do):
//   age < 60   → Scheme 1
//   age ≥ 60   → Scheme 2
//
// The 55–59 ambiguity is a legacy-citizen-member artefact. Foreign workers
// entering Malaysian employment under the expansion are always new registrants,
// so it does not apply to them and the dropdown auto-fills cleanly.
public static class SocsoSchemeAdvisor
{
    // Whole years elapsed, accounting for whether the birthday has passed yet.
    // Clamped at 0 so a mistyped future DOB can't produce a negative age.
    public static int CalculateAge(DateTime dateOfBirth, DateTime asOf)
    {
        var age = asOf.Year - dateOfBirth.Year;
        if (asOf.Month < dateOfBirth.Month ||
            (asOf.Month == dateOfBirth.Month && asOf.Day < dateOfBirth.Day))
        {
            age--;
        }

        return Math.Max(0, age);
    }

    // Null means "we can't recommend" — either no DOB on file, or the 55–59
    // window where the caller must ask the admin to pick.
    //
    // `isMalaysianCitizen` null (nationality not captured yet) falls back to the
    // Malaysian rule on purpose: surfacing a manual-choice prompt is better than
    // silently landing a 57-year-old on Scheme 1.
    public static SocsoScheme? Recommend(
        DateTime? dateOfBirth, bool? isMalaysianCitizen, DateTime asOf)
    {
        if (dateOfBirth is null) return null;

        var age = CalculateAge(dateOfBirth.Value, asOf);

        if (age >= 60) return SocsoScheme.EMPLOYMENT_INJURY_ONLY;
        if (age >= 55 && isMalaysianCitizen != false) return null;

        return SocsoScheme.EMPLOYMENT_INJURY_INVALIDITY;
    }

    // True exactly when `Recommend` returns null because of the 55–59 window —
    // i.e. there IS a DOB but the first-time-registrant rule makes it ambiguous.
    // The UI renders a "please pick manually" hint off this.
    public static bool NeedsManualChoice(
        DateTime? dateOfBirth, bool? isMalaysianCitizen, DateTime asOf)
    {
        if (dateOfBirth is null) return false;

        var age = CalculateAge(dateOfBirth.Value, asOf);
        if (age < 55 || age >= 60) return false;

        return isMalaysianCitizen != false;
    }
}
