using System.Globalization;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

// What a payslip LISTS, as opposed to what it totals: the named earnings and
// deductions lines, and the Overtime table. Pure — no EF, no clock.
//
// ONE implementation for the PDF and the employee's on-screen payslip
// (PayslipMapper), so the two can never list different things.
//
// Each list is printed only when it visibly adds up to the total it sits
// under. A YTD-imported payslip carries totals with no line items behind
// them, and a payslip generated before the overtime rates were kept has no
// rates to show; those fall back to the plain totals rather than printing
// rows that do not reconcile.
public static class PayslipDisplayLines
{
    public sealed record Line(string Label, decimal Amount);

    // One row of the Overtime table. `Rate` is the pay for ONE hour at this
    // multiplier (hourly rate × multiplier), unrounded — show it to 4 places.
    public sealed record OvertimeRow(decimal Multiplier, decimal Rate, decimal Hours, decimal Amount)
    {
        // "1.5 TIMES", "2.0 TIMES", "1.25 TIMES".
        public string Label => $"{Multiplier.ToString("0.0#", CultureInfo.InvariantCulture)} TIMES";
    }

    // ─── Earnings ───────────────────────────────────────────────────────

    // The cash lines making up the Earnings column after basic pay and
    // overtime: reimbursements and allowances as positives, gross-reducing
    // deductions (unpaid leave, an advance) as negatives. Benefits in kind are
    // excluded — they never enter gross and have their own section.
    public static List<Line> Earnings(IEnumerable<PayslipLineItem> lineItems)
    {
        var lines = new List<Line>();

        foreach (var li in lineItems)
        {
            var meta = PayrollAdjustmentCategories.Find(li.Category);

            switch (li.Kind)
            {
                case PayslipLineKind.REIMBURSEMENT:
                    lines.Add(new Line(li.Label, li.Amount));
                    break;

                case PayslipLineKind.ALLOWANCE when meta?.NonCash != true:
                    lines.Add(new Line(li.Label, li.Amount));
                    break;

                case PayslipLineKind.DEDUCTION when meta?.ReducesGross == true:
                    lines.Add(new Line(li.Label, -li.Amount));
                    break;
            }
        }

        return lines;
    }

    // Itemise only when the rows account for gross exactly. GrossPay is the
    // calculator's own figure, not a re-sum of these rows.
    public static bool EarningsReconcile(Payslip p, IEnumerable<Line> lines)
    {
        var displayed = p.ProratedPay + p.OtPay + lines.Sum(l => l.Amount);
        return Math.Abs(displayed - p.GrossPay) < 0.005m;
    }

    // ─── Deductions ─────────────────────────────────────────────────────

    // The non-statutory deductions taken from pay, each by the name the admin
    // gave it — a loan repayment, zakat, CP38, a one-off deduction — exactly
    // the rows PayslipCalculator adds into TotalDeductions. Left out:
    //   · gross-reducing ones (unpaid leave, an advance) — in Earnings;
    //   · cash-neutral ones (TP1 reliefs, zakat paid outside payroll) — they
    //     lower PCB but take nothing from pay;
    //   · Additional PCB — printed inside the "PCB / MTD" figure.
    public static List<Line> Deductions(IEnumerable<PayslipLineItem> lineItems)
    {
        var lines = new List<Line>();

        foreach (var li in lineItems)
        {
            if (li.Kind != PayslipLineKind.DEDUCTION) continue;

            var meta = PayrollAdjustmentCategories.Find(li.Category);
            if (meta is { ReducesGross: true } or { CashNeutral: true } or { AddsToStandardPcb: true })
                continue;

            lines.Add(new Line(li.Label, li.Amount));
        }

        return lines;
    }

    // TotalDeductions includes Additional PCB, which is shown in the PCB row.
    public static bool DeductionsReconcile(Payslip p, IEnumerable<Line> lines) =>
        Math.Abs(lines.Sum(l => l.Amount) - (p.TotalDeductions - p.VoluntaryPcb)) < 0.005m;

    // Zakat and CP38 taken from pay that no line item stands for — and so
    // OUTSIDE TotalDeductions. Only an imported payslip has these:
    // YtdImportService keeps the import's zakat and CP38 columns in
    // Payslip.Zakat / Cp38 alone, and its net subtracts them separately. A
    // computed payslip always has the deduct_zakat / deduct_cp38 line behind
    // the figure (inside TotalDeductions), so this is empty for it.
    //
    // Keyed on "no line of that kind at all" rather than a difference, so
    // per-line rounding can never conjure a one-sen Zakat row.
    public static List<Line> OutsideTotalDeductions(Payslip p, IEnumerable<PayslipLineItem> lineItems)
    {
        var deductionMeta = lineItems
            .Where(li => li.Kind == PayslipLineKind.DEDUCTION)
            .Select(li => PayrollAdjustmentCategories.Find(li.Category))
            .ToList();

        var lines = new List<Line>();
        if (p.Cp38 > 0m && !deductionMeta.Any(m => m is { AddsToCp38Field: true }))
            lines.Add(new Line("CP38 arrears", p.Cp38));
        if (p.Zakat > 0m && !deductionMeta.Any(m => m is { OffsetsPcb: true }))
            lines.Add(new Line("Zakat", p.Zakat));
        return lines;
    }

    // The lines that are on the payslip but not in pay: benefits in kind
    // (taxed, never paid in cash) and cash-neutral deductions (TP1 reliefs,
    // zakat paid outside payroll — they lower PCB, take nothing from pay).
    public static List<Line> NotInPay(IEnumerable<PayslipLineItem> lineItems) =>
        lineItems
            .Where(li =>
            {
                var meta = PayrollAdjustmentCategories.Find(li.Category);
                return (li.Kind == PayslipLineKind.ALLOWANCE && meta is { NonCash: true })
                       || (li.Kind == PayslipLineKind.DEDUCTION && meta is { CashNeutral: true });
            })
            .Select(li => new Line(li.Label, li.Amount))
            .ToList();

    // ─── Overtime ───────────────────────────────────────────────────────

    // One row per multiplier: the built-in normal / rest day / public holiday
    // hours and the company's own overtime types, merged where they pay the
    // same multiplier. Empty when there is nothing to break down — no
    // overtime, or a payslip from before the rates were snapshotted.
    //
    // OtPay rounds the month's overtime ONCE; each row rounds on its own, so
    // the rows can be a sen or two away from it. The difference goes on the
    // largest row, so the table always adds up to the Overtime line in
    // Earnings — the figure that was actually paid.
    public static IReadOnlyList<OvertimeRow> Overtime(Payslip p)
    {
        if (p.OtPay <= 0m
            || p.OtHourlyRate is not { } hourly
            || p.OtRateNormal is not { } normal
            || p.OtRateRest is not { } rest
            || p.OtRatePublicHoliday is not { } publicHoliday)
        {
            return [];
        }

        var parts = new List<(decimal Multiplier, decimal Hours)>
        {
            (normal, p.OtNormalHours),
            (rest, p.OtRestHours),
            (publicHoliday, p.OtPublicHours),
        };
        parts.AddRange(PayrollRunAdjustments.ParseOtCustomLines(p.OtCustomJson)
            .Select(c => (c.Multiplier, c.Hours)));

        var rows = parts
            .Where(x => x.Hours > 0m && x.Multiplier > 0m)
            .GroupBy(x => x.Multiplier)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var hours = g.Sum(x => x.Hours);
                return new OvertimeRow(g.Key, hourly * g.Key, hours, Money.Round2(hours * hourly * g.Key));
            })
            .ToList();

        if (rows.Count == 0) return [];

        // More than rounding apart means these rows don't describe what was
        // paid; show the total alone rather than a table that doesn't add up.
        var diff = p.OtPay - rows.Sum(r => r.Amount);
        if (Math.Abs(diff) > 0.005m * rows.Count) return [];

        if (diff != 0m)
        {
            var largest = rows.IndexOf(rows.MaxBy(r => r.Amount)!);
            rows[largest] = rows[largest] with { Amount = rows[largest].Amount + diff };
        }

        return rows;
    }
}
