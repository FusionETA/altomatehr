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
