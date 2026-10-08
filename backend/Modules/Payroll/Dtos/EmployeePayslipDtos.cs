namespace AltomateHR.Api.Modules.Payroll.Dtos;

// A row in the employee's own payslip list. Deliberately thinner than the full
// PayslipDto: the list is a chooser, and the detail view is one click away.
public class EmployeePayslipSummaryDto
{
    public string Id { get; set; } = string.Empty;

    public int PeriodYear { get; set; }
    public int PeriodMonth { get; set; }
    public string PeriodLabel { get; set; } = string.Empty;

    public decimal GrossPay { get; set; }
    public decimal NetPay { get; set; }

    // What the employee themselves paid — the figures they check first.
    public decimal EpfEmployee { get; set; }
    public decimal SocsoEmployee { get; set; }
    public decimal EisEmployee { get; set; }
    public decimal Pcb { get; set; }

    // When the run went live. The date the payslip became real.
    public DateTime? SubmittedAt { get; set; }
}

// A year the employee was paid in, for their own Form EA.
//
// The form declares the whole January–December year, so it is downloadable
// only once the company's year is approved through December (EaYear) — leavers included. Until
// then the row still appears, so "not yet" is visible rather than looking
// like the form is missing.
public class EmployeeEaFormDto
{
    public int Year { get; set; }

    public bool Available { get; set; }

    // How many of the months the form waits for are approved so far, and how
    // many it waits for (from the company's first run of the year through
    // December — 12 for a company that ran all year), for the "not yet" line.
    public int ApprovedMonths { get; set; }
    public int RequiredMonths { get; set; } = 12;

    // Why it is not ready yet, in words (EaYear.NotReadyReason). Null when
    // available. The server's, so the page never guesses the blocker.
    public string? NotReadyReason { get; set; }
}
