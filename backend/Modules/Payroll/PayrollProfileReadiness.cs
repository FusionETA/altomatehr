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
    public static bool IsComplete(EmployeeProfile p) =>
        IsPersonalComplete(p) && IsEmploymentComplete(p) && IsStatutoryComplete(p);

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
    private static bool IsStatutoryComplete(EmployeeProfile p)
    {
        if (p.ContributeToEpf && string.IsNullOrWhiteSpace(p.EpfNumber)) return false;
        if (p.SocsoScheme is not null && string.IsNullOrWhiteSpace(p.SocsoNumber)) return false;
        return true;
    }
}
