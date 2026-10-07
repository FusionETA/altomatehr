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
// The form declares the employee's whole year with this company, so it is
// downloadable only once every month of it is approved: January–December, or
// for someone who left that year, January up to the month they left (their
// last pay, if later). Until then the row still appears, so "not yet" is
// visible rather than looking like the form is missing.
public class EmployeeEaFormDto
{
    public int Year { get; set; }

    public bool Available { get; set; }

    // The months that must be approved: January through this one. 12 unless
    // the employee left during the year.
    public int RequiredMonths { get; set; }

    // How many of those are approved so far, for the "not yet" line.
    public int ApprovedMonths { get; set; }
}
