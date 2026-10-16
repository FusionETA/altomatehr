using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Modules.Payroll.Pdf;

namespace AltomateHR.Api.Modules.Payroll;

// The payroll summary workbook's Breakdown sheet, as data: one row per
// payslip, one column per pay item.
//
//   Emp no. | Staff | Group | Outlet
//   | Basic | U/L | Travelling | Meal | Parking | Hours | OT | Comm | Bonus | Deduction
//   | one column per other EARNING on the run   (category label, e.g. "Phone Allowance (Fixed)")
//   | one column per other DEDUCTION on the run (e.g. "Loan Repayment", "CP38 Arrears (LHDN Order)")
//   | Total Gross
//   | NOT IN TOTAL GROSS: one column per item that is not pay and not a
//     deduction from pay (see below)
//
// The fixed columns are PayItemColumns, the same mapping the timesheet export
// uses. Extra columns appear only for items someone on the run actually has,
// in the order they first appear.
//
// Every line PayrollSummaryPdf.Breakdown lists (the PDF's itemised lines, and
// what the old long-list Breakdown sheet printed) lands in exactly one column,
// so the sheet adds up to the same money — tested.
//
// Total Gross = the money columns on the row added up, deductions negative
// (Hours left out). For a generated payslip that is
// GrossPay − AbPayTimesheetXlsx.OtherDeductions: gross less every NON-statutory
// deduction taken from net pay (loans, miscellaneous, CP38, free-text
// deductions). It is deliberately not the payroll Gross on the Summary sheet,
// which takes off only the deductions the company keeps (loans, miscellaneous
// — PayrollGrossShown), not CP38 or free-text deductions.
//
// "Not in Total Gross" columns sit after Total Gross, under their own band, and
// are never added into it:
//   - statutory deductions that appear as line items — zakat taken from pay
//     (PZB) and Additional PCB. Statutory figures belong with EPF/SOCSO/EIS/PCB,
//     which stay on the Summary sheet; the timesheet export's Statutory sheet
//     classifies these two the same way (its Zakat and PCB columns).
//   - cash-neutral TP1 items — reliefs, self-paid zakat, the departure levy.
//     They lower PCB but nothing left the payslip.
//   - benefits in kind — taxable, but never cash, never in gross.
// Shown (with deductions negative) so nothing the payroll listed is dropped.
public static class PayrollSummaryBreakdown
{
    public enum ColumnKind
    {
        Money,       // added into Total Gross
        Hours,       // a quantity, not money
        TotalGross,
        NotInTotal,  // shown for information, never added into Total Gross
    }

    public sealed record Column(string Header, ColumnKind Kind);

    public sealed record Row
    {
        public required string EmployeeNumber { get; init; }
        public required string Staff { get; init; }
        public required string Group { get; init; }
        public required string Outlet { get; init; }

        // One per Table.Columns, in the same order.
        public required decimal[] Values { get; init; }
    }

    public sealed record Table(IReadOnlyList<Column> Columns, IReadOnlyList<Row> Rows)
    {
        public int TotalGrossIndex => Columns.ToList().FindIndex(c => c.Kind == ColumnKind.TotalGross);
    }

    public const string TotalGrossHeader = "Total Gross";
    public const string ClaimsHeader = "Claims";

    public const string TotalGrossNote =
        "Total Gross = all the money columns on this row added up (deductions are shown as negative). "
        + "It can differ from the payroll Gross on the Summary sheet, which takes off only the "
        + "deductions the company keeps (loans, miscellaneous) — not CP38 or other deductions taken "
        + "from net pay.";

    private enum Group { Earning, Deduction, NotInTotal }

    // Where one line item goes: which group, the column's identity (so two
    // employees' phone allowances share a column), its header, and the signed
    // amount.
    private sealed record Placement(Group Group, string Key, string Header, decimal Amount);

    public static Table Build(PayrollDocumentModel model)
    {
        // Pass 1: place every line that has no fixed column, collecting the
        // extra columns in first-appearance order within each group.
        var groups = new Dictionary<Group, List<(string Key, string Header)>>
        {
            [Group.Earning] = [],
            [Group.Deduction] = [],
            [Group.NotInTotal] = [],
        };
        var placed = new List<List<Placement>>();

        foreach (var r in model.Rows)
        {
            var items = model.LineItems.GetValueOrDefault(r.Payslip.Id) ?? [];
            var mine = new List<Placement>();
            foreach (var li in items)
            {
                if (PayItemColumns.HasColumn(li.Category)) continue;

                var p = Place(li);
                mine.Add(p);
                if (!groups[p.Group].Any(c => c.Key == p.Key))
                    groups[p.Group].Add((p.Key, p.Header));
            }
            placed.Add(mine);
        }

        // Columns: fixed, extra earnings, extra deductions, Total Gross, then
        // the not-in-total ones. Headers made unique (a free-text deduction
        // called "Deduction" must not look like the fixed column).
        var columns = new List<Column>();
        var keyIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Emp no.", "Staff", "Group", "Outlet", TotalGrossHeader,
        };

        for (var i = 0; i < PayItemColumns.Headers.Count; i++)
        {
            var header = PayItemColumns.Headers[i];
            used.Add(header);
            columns.Add(new Column(header, i == PayItemColumns.HoursIndex ? ColumnKind.Hours : ColumnKind.Money));
        }

        void AddExtras(Group g, ColumnKind kind)
        {
            foreach (var (key, header) in groups[g])
            {
                keyIndex[key] = columns.Count;
                columns.Add(new Column(Unique(header, used), kind));
            }
        }

        AddExtras(Group.Earning, ColumnKind.Money);
        AddExtras(Group.Deduction, ColumnKind.Money);
        var totalGrossIndex = columns.Count;
        columns.Add(new Column(TotalGrossHeader, ColumnKind.TotalGross));
        AddExtras(Group.NotInTotal, ColumnKind.NotInTotal);

        // Pass 2: the rows.
        var rows = new List<Row>(model.Rows.Count);
        for (var i = 0; i < model.Rows.Count; i++)
        {
            var r = model.Rows[i];
            var p = r.Payslip;
            var items = model.LineItems.GetValueOrDefault(p.Id) ?? [];
            var values = new decimal[columns.Count];

            var fixedValues = PayItemColumns.Of(p, items).InOrder();
            Array.Copy(fixedValues, values, fixedValues.Length);

            foreach (var pl in placed[i])
                values[keyIndex[pl.Key]] += pl.Amount;

            values[totalGrossIndex] = Money.Round2(Enumerable.Range(0, columns.Count)
                .Where(c => columns[c].Kind == ColumnKind.Money)
                .Sum(c => values[c]));

            rows.Add(new Row
            {
                EmployeeNumber = p.SnapshotEmployeeNumber ?? string.Empty,
                Staff = p.SnapshotName,
                Group = PayItemColumns.GroupOf(r),
                Outlet = PayItemColumns.OutletOf(r),
                Values = values,
            });
        }

        return new Table(columns, rows);
    }

    // The same classification PayrollSummaryPdf.Breakdown and the payslip
    // calculator use: a benefit in kind is a non-cash ALLOWANCE; a TP1 item
    // is CashNeutral; zakat from pay and Additional PCB are the statutory
    // deductions (AbPayTimesheetXlsx.ZakatFromPay / Payslip.VoluntaryPcb).
    private static Placement Place(PayslipLineItem li)
    {
        var meta = PayrollAdjustmentCategories.Find(li.Category);

        // A category the catalogue knows by code; otherwise the line's own
        // label (free-text deductions, a code an older system wrote).
        string CategoryKey() => meta is not null
            ? "cat:" + meta.Code
            : "label:" + li.Kind + ":" + li.Label.Trim().ToLowerInvariant();
        string CategoryHeader() => meta?.Label
                                   ?? (string.IsNullOrWhiteSpace(li.Label) ? li.Kind.ToString() : li.Label.Trim());

        if (li.Kind == PayslipLineKind.DEDUCTION)
        {
            var notInTotal = meta is { CashNeutral: true }
                             || meta is { OffsetsPcb: true }
                             || meta is { AddsToStandardPcb: true };
            return new Placement(
                notInTotal ? Group.NotInTotal : Group.Deduction, CategoryKey(), CategoryHeader(), -li.Amount);
        }

        if (li.Kind == PayslipLineKind.ALLOWANCE && meta is { NonCash: true })
        {
            return new Placement(
                Group.NotInTotal, CategoryKey(), $"{meta.Label} (non-cash)", li.Amount);
        }

        // An attached claim carries no category and its claim's own title, so
        // all of them share one column rather than one per claim.
        if (li.Kind == PayslipLineKind.REIMBURSEMENT && meta is null)
            return new Placement(Group.Earning, "claims", ClaimsHeader, li.Amount);

        return new Placement(Group.Earning, CategoryKey(), CategoryHeader(), li.Amount);
    }

    private static string Unique(string header, HashSet<string> used)
    {
        var candidate = header;
        for (var n = 2; !used.Add(candidate); n++) candidate = $"{header} ({n})";
        return candidate;
    }
}
