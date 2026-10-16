using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Leave.Entities;

namespace AltomateHR.Api.Modules.Leave;

// Who may take a leave type — maternity for women, paternity for men, by
// default (LeaveType.EligibleGender). Pure, so the rule lives in one place.
public static class LeaveEligibility
{
    // No restriction, or no gender on the employee's profile: missing data
    // must not block anyone from leave they may well be entitled to.
    public static bool Allows(LeaveType type, Gender? gender) =>
        type.EligibleGender is null || gender is null || type.EligibleGender == gender;

    public static string Refusal(LeaveType type) =>
        $"{type.Name} is only for {(type.EligibleGender == Gender.FEMALE ? "female" : "male")} employees.";
}
