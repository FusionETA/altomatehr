using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

// The gross the payroll screens and summaries SHOW: pay less the deductions
// the company keeps (Miscellaneous / Other, Loan Repayment — the
// RetainedByEmployer categories), which is how PayrollPanda shows it. Muhammad
// Afiq's September: earnings 9,555, a RM 175 other deduction → shown 9,380.
//
// Display only. GrossPay stays what was earned, because that is what EPF,
// SOCSO, EIS and PCB are worked out on, what the KWSP / PERKESO files carry as
// the wage, and what the EA form reports — PayrollPanda's own EPF for that
// month (1,056) is on the full 9,555 too.
//
// Read off the cost-to-employer identity rather than re-summing line items:
// cost = GrossPay + employer EPF/SOCSO/EIS + HRDF − kept deductions (see
// PayslipCalculator and YtdImportService), so a YTD-imported month with no
// line items behind it still comes out right, and a payslip stored before
// kept deductions came off cost simply shows its GrossPay. Clamped, so odd
// stored figures can only ever fall back to GrossPay.
public static class PayrollGrossShown
{
    public static decimal Of(Payslip p) =>
        Shown(p.GrossPay, p.TotalCostToEmployer, p.EpfEmployer + p.SocsoEmployer + p.EisEmployer + p.Hrdf);

    public static decimal Of(PayrollRun r) =>
        Shown(r.TotalGross, r.TotalCostToEmployer,
            r.TotalEmployerEpf + r.TotalEmployerSocso + r.TotalEmployerEis + r.TotalHrdf);

    private static decimal Shown(decimal gross, decimal cost, decimal employerContributions)
    {
        var kept = Math.Clamp(Money.Round2(gross + employerContributions - cost), 0m, Math.Max(0m, gross));
        return Money.Round2(gross - kept);
    }
}
