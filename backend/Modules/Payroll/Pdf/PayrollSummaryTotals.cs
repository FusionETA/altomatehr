using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll.Pdf;

// The payroll summary's run totals and its closing summary block, shared by
// the PDF (PayrollSummaryPdf) and the Excel copy (PayrollSummaryXlsx) so the
// two can never print different figures for the same run.
//
// Summed from the payslips shown rather than read off the run's cached
// figures: if those ever disagreed, printing the cached one would hide it —
// the payslips are the source of truth.
public sealed class PayrollSummaryTotals
{
    public decimal Gross { get; private set; }
    public decimal Bik { get; private set; }
    public decimal Pcb { get; private set; }
    public decimal EpfEmp { get; private set; }
    public decimal SocsoEmp { get; private set; }
    public decimal EisEmp { get; private set; }
    public decimal SkbbkEmp { get; private set; }
    public decimal Net { get; private set; }
    public decimal EpfEr { get; private set; }
    public decimal SocsoEr { get; private set; }
    public decimal EisEr { get; private set; }
    public decimal Hrdf { get; private set; }
    public decimal Cost { get; private set; }
    public decimal Zakat { get; private set; }
    public decimal HrdfWage { get; private set; }
    public int HrdfCount { get; private set; }
    public int Employees { get; private set; }

    public static PayrollSummaryTotals Of(IEnumerable<Payslip> payslips)
    {
        var t = new PayrollSummaryTotals();
        foreach (var p in payslips)
        {
            t.Employees++;
            t.Gross += p.GrossPay;
            t.Bik += p.TotalBenefitsInKind;
            t.Pcb += p.Pcb;
            t.EpfEmp += p.EpfEmployee;
            t.SocsoEmp += p.SocsoEmployee;
            t.EisEmp += p.EisEmployee;
            t.SkbbkEmp += p.SkbbkEmployee;
            t.Net += p.NetPay;
            t.EpfEr += p.EpfEmployer;
            t.SocsoEr += p.SocsoEmployer;
            t.EisEr += p.EisEmployer;
            t.Hrdf += p.Hrdf;
            t.Cost += p.TotalCostToEmployer;
            t.Zakat += p.Zakat;
            t.HrdfWage += p.HrdfWage;
            if (p.Hrdf > 0m) t.HrdfCount++;
        }
        return t;
    }

    // One line of the closing summary block. A count prints as a whole number;
    // everything else is ringgit.
    public sealed record SummaryLine(string Label, decimal Value, bool IsCount = false);

    // The summary block, in the previous system's order and wording.
    public IReadOnlyList<SummaryLine> SummaryLines()
    {
        var rows = new List<SummaryLine>
        {
            new("Number of employees", Employees, IsCount: true),
            new("Total employee net pay", Net),
            new("Total PCB payment", Pcb),
            new("Employees subject to HRDF", HrdfCount, IsCount: true),
            new("Total wages subject to HRDF", HrdfWage),
            new("Total EPF payment", EpfEmp + EpfEr),
            new("Total SOCSO payment", SocsoEmp + SocsoEr),
            new("Total EIS payment", EisEmp + EisEr),
        };

        // SKBBK started Jun 2026; earlier months should not show a zero line.
        if (SkbbkEmp > 0m) rows.Add(new("Total SKBBK payment", SkbbkEmp));
        rows.Add(new("Total HRDF payment", Hrdf));
        rows.Add(new("Total Zakat payment", Zakat));
        if (Bik > 0m) rows.Add(new("Total Benefits in Kind (non-cash, for tax)", Bik));

        return rows;
    }
}
