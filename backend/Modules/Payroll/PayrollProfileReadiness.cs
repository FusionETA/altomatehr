using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Policies.Entities;   // SalaryType

namespace AltomateHR.Api.Modules.Payroll;

// Whether an employee's profile carries the fields a payroll run needs.
//
// Mirrors the monolith's isPayrollProfileComplete (Personal ∧ Employment ∧
// Statutory) — one gate so the "ready for payroll" badge and the run
// generator can never disagree: the employee an admin sees flagged as
// incomplete is exactly the one a run leaves out, rather than paying them a
// wrong or all-zero figure.
//
// Bank details are deliberately NOT required — they are needed to DISBURSE
// pay, not to CALCULATE it, so a missing account never blocks a run.
public static class PayrollProfileReadiness
{
    public static bool IsComplete(EmployeeProfile p) => IsComplete(p, DateTime.UtcNow.Date);

    // `asOf` is the day the age-based SOCSO scheme is judged on. A run passes
    // its period end — the date generation prices the payslip at — so someone
    // who turns 55 inside the month is not called ready by the picker and then
    // paid with no SOCSO. Only a view that belongs to no month (the profile
    // badge, the roster) uses today.
    public static bool IsComplete(EmployeeProfile p, DateTime asOf) => IncompleteSections(p, asOf).Count == 0;

    // The same verdict, but saying WHICH of the three sections is short.
    //
    // Exposed so a roster can flag an incomplete profile without the admin
    // opening each one to find out — and derived from the very same three
    // predicates as IsComplete, so a list badge and a run's inclusion rule
    // cannot disagree about who is ready.
    //
    // Section names, not field names, on purpose: the per-field labels live in
    // the frontend's own section map, and restating them here would be a second
    // list to keep in step.
    public static IReadOnlyList<string> IncompleteSections(EmployeeProfile p) =>
        IncompleteSections(p, DateTime.UtcNow.Date);

    public static IReadOnlyList<string> IncompleteSections(EmployeeProfile p, DateTime asOf)
    {
        var sections = new List<string>(3);
        if (!IsPersonalComplete(p)) sections.Add("Personal");
        if (!IsEmploymentComplete(p)) sections.Add("Employment");
        if (!IsStatutoryComplete(p, asOf)) sections.Add("Statutory");
        return sections;
    }

    // The last day of a payroll month — the as-of date generation uses.
    public static DateTime PeriodEnd(int year, int month) =>
        new(year, month, DateTime.DaysInMonth(year, month));

    // Identity + demographics the PCB branch needs. Spouse-working is required
    // when married because it drives the spouse-relief path (S = RM 4,000 with
    // no spouse income); without it PCB falls back to the safer no-relief path
    // and over-withholds.
    private static bool IsPersonalComplete(EmployeeProfile p)
    {
        if (p.Gender is null) return false;
        if (p.DateOfBirth is null) return false;
        if (string.IsNullOrWhiteSpace(p.Nationality)) return false;
        if (p.IdType is null) return false;
        if (string.IsNullOrWhiteSpace(p.IdNumber)) return false;
        if (p.MaritalStatus is null) return false;
        if (p.MaritalStatus == AltomateHR.Api.Modules.Employees.Entities.MaritalStatus.MARRIED
            && p.SpouseWorking is null)
            return false;
        return true;
    }

    // The salary structure the calc engine consumes, plus a join date for
    // first/last-month proration. A zero salary is allowed here — that is an
    // intentional "excluded from payroll" opt-out, not an incomplete profile.
    private static bool IsEmploymentComplete(EmployeeProfile p)
    {
        if (p.SalaryType == SalaryType.MONTHLY && (p.MonthlySalary is null || p.MonthlySalary < 0))
            return false;
        if (p.SalaryType == SalaryType.HOURLY && (p.HourlyRate is null || p.HourlyRate < 0))
            return false;
        if (p.JoinDate is null) return false;
        return true;
    }

    // Numbers the statutory files need: an EPF number when the employee
    // contributes, a SOCSO number when a scheme is set. The income-tax number
    // is deliberately NOT gated — joiners are onboarded before LHDN issues a
    // TIN, and the CP39/PCB generator surfaces its own error at file-build time
    // when the TIN is actually required.
    private static bool IsStatutoryComplete(EmployeeProfile p, DateTime asOf)
    {
        if (p.ContributeToEpf && string.IsNullOrWhiteSpace(p.EpfNumber)) return false;
        if (p.SocsoScheme is not null && p.SocsoScheme != SocsoScheme.NOT_COVERED
            && string.IsNullOrWhiteSpace(p.SocsoNumber)) return false;
        // No scheme chosen and none can be suggested (a Malaysian aged 55–59
        // must be picked by hand): incomplete, so a run leaves them out rather
        // than paying them with no SOCSO.
        if (SocsoSchemeAdvisor.Effective(p.SocsoScheme, p.DateOfBirth, p.Nationality, asOf) is null
            && p.DateOfBirth is not null) return false;
        return true;
    }
}
