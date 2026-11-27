using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

// The wage SOCSO and EIS were contributed on, for a payslip already written —
// PERKESO's "Employee salary". The SOCSO counterpart of PayslipEpfWage.
//
// PayslipCalculator stamps it onto `Payslip.SocsoWage`, and that stored figure
// always wins. Payslips generated before the column existed have none, so it is
// rebuilt from the prorated basic, the overtime (a SOCSO/EIS wage, unlike EPF)
// and the line items' subject-to-SOCSO flags. PayslipEpfWageTests pins the
// rebuild to the calculator, for both wages.
public static class PayslipSocsoWage
{
    public static decimal For(Payslip payslip, IEnumerable<PayslipLineItem> lineItems) =>
        payslip.SocsoWage ?? Rebuild(payslip.ProratedPay, payslip.OtPay, lineItems);

    public static decimal Rebuild(
        decimal proratedPay, decimal otPay, IEnumerable<PayslipLineItem> lineItems) =>
        PayslipWageLines.Rebuild(proratedPay + otPay, lineItems, line => line.SubjectToSocso);
}
