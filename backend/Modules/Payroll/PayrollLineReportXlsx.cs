using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Modules.Payroll.Pdf;
using ClosedXML.Excel;

namespace AltomateHR.Api.Modules.Payroll;

// The Allowance and Deduction reports: one row per employee, one column per
// item as it is labelled on the payslip, so finance can see who received what
// without opening every payslip. Amounts are real numbers, so the sheet sums
// and filters itself.
//
// Read straight from the run's payslip line items — the same snapshot the
// payslips print — so the report can never disagree with them.
public static class PayrollLineReportXlsx
{
    public const string ContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public enum Report { Allowances, Deductions }

    // A column of the report. A column with a Note is listed but kept out of
    // the total, so the total matches the payslip: benefits in kind ("BIK")
    // were never paid in cash, and unpaid leave / advances / salary
    // adjustments ("from gross") come off gross pay, not off deductions.
    public sealed record Column(string Label, string? Note)
    {
        public bool InTotal => Note is null;
        public string Header => Note is null ? Label : $"{Label} ({Note})";
    }

    // What counts as an allowance: every ALLOWANCE line, benefits in kind
    // flagged. Claims (REIMBURSEMENT) are paying back money spent, not pay.
    public static bool IsAllowance(PayslipLineItem li) => li.Kind == PayslipLineKind.ALLOWANCE;

    // What counts as an "other" deduction: money that really leaves the
    // payslip and is not statutory. Left out:
    //   - CashNeutral rows (TP1 reliefs, self-paid zakat): only lower PCB,
    //     nothing is deducted;
    //   - additional PCB and CP38: tax, which the statutory files already carry.
    public static bool IsOtherDeduction(PayslipLineItem li)
    {
        if (li.Kind != PayslipLineKind.DEDUCTION) return false;
        var meta = PayrollAdjustmentCategories.Find(li.Category);
        return meta is null || !(meta.CashNeutral || meta.AddsToStandardPcb || meta.AddsToCp38Field);
    }

    public static bool IsNonCash(PayslipLineItem li) =>
        li.Kind == PayslipLineKind.ALLOWANCE && PayrollAdjustmentCategories.Find(li.Category)?.NonCash == true;

    public static bool ReducesGross(PayslipLineItem li) =>
        li.Kind == PayslipLineKind.DEDUCTION && PayrollAdjustmentCategories.Find(li.Category)?.ReducesGross == true;

    // Listed, but not added into the report's total (see Column).
    public static string? NoteFor(PayslipLineItem li) =>
        IsNonCash(li) ? "BIK" : ReducesGross(li) ? "from gross" : null;

    public static bool InTotal(PayslipLineItem li) => NoteFor(li) is null;

    // The lines the report covers, by payslip id.
    public static Dictionary<string, List<PayslipLineItem>> Select(PayrollDocumentModel model, Report report)
    {
        Func<PayslipLineItem, bool> keep = report == Report.Allowances ? IsAllowance : IsOtherDeduction;
        return model.LineItems.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Where(keep).Where(li => li.Amount != 0m).ToList(),
            StringComparer.Ordinal);
    }

    public static bool HasAny(PayrollDocumentModel model, Report report) =>
        Select(model, report).Values.Any(l => l.Count > 0);

    // One column per payslip label, in the order they first appear on the
    // payslips; items in the total first, then the noted ones.
    public static IReadOnlyList<Column> Columns(IEnumerable<PayslipLineItem> lines)
    {
        var seen = new Dictionary<string, Column>(StringComparer.Ordinal);
        foreach (var li in lines.OrderBy(l => l.SortOrder))
            seen.TryAdd(Key(li), new Column(li.Label.Trim(), NoteFor(li)));
        return seen.Values.Where(c => c.InTotal).Concat(seen.Values.Where(c => !c.InTotal)).ToList();
    }

    public static byte[] Render(PayrollDocumentModel model, Report report)
    {
        var byPayslip = Select(model, report);
        var rows = model.Rows
            .Where(r => byPayslip.TryGetValue(r.Payslip.Id, out var l) && l.Count > 0)
            .ToList();
        var columns = Columns(rows.SelectMany(r => byPayslip[r.Payslip.Id]));

        var name = report == Report.Allowances ? "Allowances" : "Deductions";
        var totalLabel = report == Report.Allowances ? "Total allowances (RM)" : "Total deductions (RM)";

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(name);

        sheet.Cell(1, 1).Value = $"{model.OrganizationName} — {name.ToLowerInvariant()}, {model.PeriodLabel}";
        sheet.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(13);
        sheet.Cell(2, 1).Value = report == Report.Allowances
            ? $"{model.StatusLabel}. Every allowance on this run's payslips. Benefits in kind are listed but not in the total — they are not paid in cash."
            : $"{model.StatusLabel}. Deductions other than EPF, SOCSO, EIS and PCB. Items marked \"from gross\" (unpaid leave, advances, salary adjustments) are listed but not in the total — the payslip takes them off gross pay.";
        sheet.Cell(2, 1).Style.Font.SetItalic().Font.SetFontColor(XLColor.Gray);

        const int headerRow = 4;
        var headers = new List<string> { "Emp no.", "Employee" };
        headers.AddRange(columns.Select(c => c.Header));
        headers.Add(totalLabel);
        for (var c = 0; c < headers.Count; c++)
        {
            var cell = sheet.Cell(headerRow, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#EEE8F5"));
            cell.Style.Alignment.SetWrapText();
        }

        var totalCol = columns.Count + 3;
        var r = headerRow + 1;
        foreach (var row in rows)
        {
            var lines = byPayslip[row.Payslip.Id];
            sheet.Cell(r, 1).Value = row.EmployeeCode;
            sheet.Cell(r, 2).Value = row.EmployeeName;
            for (var c = 0; c < columns.Count; c++)
            {
                var amount = lines.Where(l => Key(l) == Key(columns[c])).Sum(l => l.Amount);
                if (amount != 0m) Money(sheet.Cell(r, c + 3), amount);
            }
            Money(sheet.Cell(r, totalCol), lines.Where(InTotal).Sum(l => l.Amount));
            sheet.Cell(r, totalCol).Style.Font.SetBold();
            r++;
        }

        // Column totals.
        sheet.Cell(r, 2).Value = $"Total ({rows.Count})";
        sheet.Cell(r, 2).Style.Font.SetBold();
        var all = rows.SelectMany(x => byPayslip[x.Payslip.Id]).ToList();
        for (var c = 0; c < columns.Count; c++)
            Money(sheet.Cell(r, c + 3), all.Where(l => Key(l) == Key(columns[c])).Sum(l => l.Amount));
        Money(sheet.Cell(r, totalCol), all.Where(InTotal).Sum(l => l.Amount));
        sheet.Range(r, 1, r, totalCol).Style.Font.SetBold()
            .Border.SetTopBorder(XLBorderStyleValues.Thin);

        sheet.Columns(1, totalCol).AdjustToContents(headerRow + 1, r);
        sheet.Column(1).Width = Math.Max(sheet.Column(1).Width, 10);
        for (var c = 3; c <= totalCol; c++)
            sheet.Column(c).Width = Math.Clamp(sheet.Column(c).Width, 14, 28);
        sheet.SheetView.FreezeRows(headerRow);
        sheet.SheetView.FreezeColumns(2);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void Money(IXLCell cell, decimal amount)
    {
        cell.Value = amount;
        cell.Style.NumberFormat.Format = "#,##0.00";
    }

    // Two lines with the same label are the same column; a noted and an
    // un-noted item that happen to share a label are not.
    private static string Key(PayslipLineItem li) => $"{NoteFor(li)}:{li.Label.Trim()}";
    private static string Key(Column c) => $"{c.Note}:{c.Label}";
}
