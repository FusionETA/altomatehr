using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

// The wage EPF was contributed on, for a payslip already written.
//
// PayslipCalculator stamps it onto `Payslip.EpfWage`, and that stored figure
// always wins. Payslips generated before the column existed have none, so it
// is rebuilt from what they DID snapshot: the prorated
// basic and each line item's category and subject-to-EPF flag — the same
// routing PayslipCalculator.RouteAllowance / RouteDeduction applies:
//
//   + every cash ALLOWANCE flagged subject to EPF (benefits in kind never are)
//   − every DEDUCTION whose category reduces the base and is subject to EPF
//
// Reimbursements (claims) and overtime never enter it. PayslipEpfWageTests
// pins the rebuild to the calculator, so the two cannot drift apart unnoticed.
public static class PayslipEpfWage
{
    public static decimal For(Payslip payslip, IEnumerable<PayslipLineItem> lineItems) =>
        payslip.EpfWage ?? Rebuild(payslip.ProratedPay, lineItems);

    public static decimal Rebuild(decimal proratedPay, IEnumerable<PayslipLineItem> lineItems) =>
        PayslipWageLines.Rebuild(proratedPay, lineItems, line => line.SubjectToEpf);
}

// How a payslip's line items move one agency's wage base, given which flag that
// agency reads — the routing PayslipCalculator.RouteAllowance / RouteDeduction
// apply, for every agency alike:
//
//   + every cash ALLOWANCE flagged subject to it (benefits in kind never are)
//   − every DEDUCTION whose catalogue category reduces the base and is subject
//
// Reimbursements (claims) never move any base.
internal static class PayslipWageLines
{
    public static decimal Rebuild(
        decimal start, IEnumerable<PayslipLineItem> lineItems, Func<PayslipLineItem, bool> subject)
    {
        var wage = start;

        foreach (var line in lineItems)
        {
            if (!subject(line)) continue;
            var meta = PayrollAdjustmentCategories.Find(line.Category);

            switch (line.Kind)
            {
                case PayslipLineKind.ALLOWANCE when meta?.NonCash != true:
                    wage += line.Amount;
                    break;

                // A deduction with no catalogue category (a manual one) never
                // touched the base — only `ReducesBase` rows do.
                case PayslipLineKind.DEDUCTION when meta?.ReducesBase == true:
                    wage -= line.Amount;
                    break;
            }
        }

        return Money.Round2(Math.Max(0m, wage));
    }
}
