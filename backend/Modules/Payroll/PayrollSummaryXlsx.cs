using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Modules.Payroll.Pdf;
using ClosedXML.Excel;

namespace AltomateHR.Api.Modules.Payroll;

// The Payroll Summary as a workbook — the same run, the same figures as
// PayrollSummaryPdf, for finance to filter, sort and paste from:
//
//   Summary     header block, then one row per employee with the PDF's
//               columns (GROSS, employee PCB/EPF/SOCSO/EIS/SKBBK, NET,
//               employer EPF/SOCSO/EIS/HRDF, COST) and a totals row.
//   Totals      the PDF's closing summary block (headcount, net, PCB, HRDF,
//               EPF/SOCSO/EIS/SKBBK, zakat, BIK).
//   Breakdown   each employee's itemised pay — base salary, overtime, every
//               line item signed by kind — the lines the PDF prints under
//               their name.
//
// Every figure comes from the payslips via PayrollSummaryTotals and
// PayrollSummaryPdf.Breakdown, the code the PDF itself uses, so the two cannot
// disagree. Totals are written as VALUES rather than formulas: they are the
// PDF's figures, not a recomputation.
public static class PayrollSummaryXlsx
{
    public const string ContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public const string SummarySheet = "Summary";
    public const string TotalsSheet = "Totals";
    public const string BreakdownSheet = "Breakdown";

    // Row of the employee table's column headings on the Summary sheet; the
    // employee rows start on the next one.
    public const int HeaderRow = 7;

    private const string MoneyFormat = "#,##0.00";
    private const string DateTimeFormat = "dd/mm/yyyy h:mm AM/PM";
    private const string DateFormat = "dd/mm/yyyy";

    private static readonly XLColor HeadFill = XLColor.FromHtml("#F1F5F9");
    private static readonly XLColor EmpFill = XLColor.FromHtml("#ECFEFF");
    private static readonly XLColor EmpInk = XLColor.FromHtml("#0E7490");
    private static readonly XLColor ErFill = XLColor.FromHtml("#FFF7ED");
    private static readonly XLColor ErInk = XLColor.FromHtml("#C2410C");
    private static readonly XLColor Muted = XLColor.FromHtml("#64748B");

    private enum Band { None, Emp, Er }

    // The PDF's columns, in its order. Employee number, name and position
    // are separate columns here (one cell in the PDF) so they filter.
    private static readonly (string Header, Band Band, Func<Payslip, decimal> Value, Func<PayrollSummaryTotals, decimal> Total)[] Amounts =
    [
        ("Gross", Band.None, p => p.GrossPay, t => t.Gross),
        ("PCB", Band.Emp, p => p.Pcb, t => t.Pcb),
        ("EPF (employee)", Band.Emp, p => p.EpfEmployee, t => t.EpfEmp),
        ("SOCSO (employee)", Band.Emp, p => p.SocsoEmployee, t => t.SocsoEmp),
        ("EIS (employee)", Band.Emp, p => p.EisEmployee, t => t.EisEmp),
        ("SKBBK (employee)", Band.Emp, p => p.SkbbkEmployee, t => t.SkbbkEmp),
        ("Net", Band.None, p => p.NetPay, t => t.Net),
        ("EPF (employer)", Band.Er, p => p.EpfEmployer, t => t.EpfEr),
        ("SOCSO (employer)", Band.Er, p => p.SocsoEmployer, t => t.SocsoEr),
        ("EIS (employer)", Band.Er, p => p.EisEmployer, t => t.EisEr),
        ("HRDF", Band.Er, p => p.Hrdf, t => t.Hrdf),
        ("Cost to employer", Band.None, p => p.TotalCostToEmployer, t => t.Cost),
    ];

    private static readonly string[] IdentityHeaders = ["Emp no.", "Employee", "Position"];

    public static byte[] Render(PayrollDocumentModel model)
    {
        var payslips = model.Rows.Select(r => r.Payslip).ToList();
        var totals = PayrollSummaryTotals.Of(payslips);

        using var workbook = new XLWorkbook();
        WriteSummary(workbook.AddWorksheet(SummarySheet), model, payslips, totals);
        WriteTotals(workbook.AddWorksheet(TotalsSheet), model, totals);
        WriteBreakdown(workbook.AddWorksheet(BreakdownSheet), model, payslips);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteSummary(
        IXLWorksheet sheet, PayrollDocumentModel model, List<Payslip> payslips, PayrollSummaryTotals totals)
    {
        var lastCol = IdentityHeaders.Length + Amounts.Length;

        // ── Header block ──
        sheet.Cell(1, 1).Value = model.OrganizationName;
        sheet.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);
        sheet.Cell(2, 1).Value = $"Payroll summary — {model.PeriodLabel}";
        sheet.Cell(2, 1).Style.Font.SetFontSize(11).Font.SetFontColor(Muted);

        Label(sheet.Cell(3, 1), "Status");
        sheet.Cell(3, 2).Value = model.StatusLabel;
        Label(sheet.Cell(3, 4), "Approved");
        if (model.Run.SubmittedAt is { } approved)
        {
            sheet.Cell(3, 5).Value = MalaysiaTime(approved).Date;
            sheet.Cell(3, 5).Style.NumberFormat.Format = DateFormat;
        }
        else
        {
            sheet.Cell(3, 5).Value = "—";
        }
        sheet.Cell(3, 5).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left);

        Label(sheet.Cell(4, 1), "Generated");
        sheet.Cell(4, 2).Value = model.GeneratedAt;
        sheet.Cell(4, 2).Style.NumberFormat.Format = DateTimeFormat;
        sheet.Cell(4, 2).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left);
        Label(sheet.Cell(4, 4), "Employees");
        sheet.Cell(4, 5).Value = totals.Employees;
        sheet.Cell(4, 5).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left);

        // ── Contribution bands over the employee / employer columns ──
        const int bandRow = HeaderRow - 1;
        BandCell(sheet, bandRow, Band.Emp, "Employee contributions");
        BandCell(sheet, bandRow, Band.Er, "Employer contributions");

        // ── Column headings ──
        for (var c = 0; c < IdentityHeaders.Length; c++)
            Heading(sheet.Cell(HeaderRow, c + 1), IdentityHeaders[c], Band.None);
        for (var c = 0; c < Amounts.Length; c++)
        {
            var cell = sheet.Cell(HeaderRow, IdentityHeaders.Length + c + 1);
            Heading(cell, Amounts[c].Header, Amounts[c].Band);
            cell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
        }

        // ── One row per employee ──
        var r = HeaderRow + 1;
        foreach (var p in payslips)
        {
            // Text, so "00027" keeps its leading zeros.
            sheet.Cell(r, 1).SetValue(p.SnapshotEmployeeNumber ?? string.Empty);
            sheet.Cell(r, 2).Value = p.SnapshotName;
            sheet.Cell(r, 3).Value = p.SnapshotPosition ?? string.Empty;

            for (var c = 0; c < Amounts.Length; c++)
                Money(sheet.Cell(r, IdentityHeaders.Length + c + 1), Amounts[c].Value(p), Amounts[c].Band);
            r++;
        }
        var lastEmployeeRow = r - 1;

        // ── Totals row ──
        var totalsRow = r;
        sheet.Cell(totalsRow, 2).Value = $"Total ({totals.Employees} employee{(totals.Employees == 1 ? "" : "s")})";
        for (var c = 0; c < Amounts.Length; c++)
            Money(sheet.Cell(totalsRow, IdentityHeaders.Length + c + 1), Amounts[c].Total(totals), Amounts[c].Band);

        var totalsRange = sheet.Range(totalsRow, 1, totalsRow, lastCol);
        totalsRange.Style.Font.SetBold();
        totalsRange.Style.Border.SetTopBorder(XLBorderStyleValues.Thin);
        totalsRange.Style.Border.SetBottomBorder(XLBorderStyleValues.Double);

        // Filter over the employees only, so sorting never moves the totals.
        sheet.Range(HeaderRow, 1, Math.Max(lastEmployeeRow, HeaderRow), lastCol).SetAutoFilter();
        sheet.SheetView.FreezeRows(HeaderRow);
        sheet.SheetView.FreezeColumns(2);

        sheet.Column(1).Width = 11;
        sheet.Column(2).Width = 34;
        sheet.Column(3).Width = 24;
        for (var c = 0; c < Amounts.Length; c++)
            sheet.Column(IdentityHeaders.Length + c + 1).Width = Math.Max(13, Amounts[c].Header.Length + 4);

        sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        sheet.PageSetup.FitToPages(1, 0);
        sheet.PageSetup.SetRowsToRepeatAtTop(HeaderRow - 1, HeaderRow);
    }

    private static void WriteTotals(IXLWorksheet sheet, PayrollDocumentModel model, PayrollSummaryTotals totals)
    {
        sheet.Cell(1, 1).Value = $"{model.OrganizationName} — payroll totals, {model.PeriodLabel}";
        sheet.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(13);

        const int headerRow = 3;
        Heading(sheet.Cell(headerRow, 1), "Item", Band.None);
        Heading(sheet.Cell(headerRow, 2), "Amount (RM)", Band.None);
        sheet.Cell(headerRow, 2).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

        var r = headerRow + 1;
        foreach (var line in totals.SummaryLines())
        {
            sheet.Cell(r, 1).Value = line.Label;
            if (line.IsCount)
            {
                sheet.Cell(r, 2).Value = (int)line.Value;
                sheet.Cell(r, 2).Style.NumberFormat.Format = "0";
            }
            else
            {
                Money(sheet.Cell(r, 2), line.Value, Band.None);
            }
            r++;
        }

        sheet.SheetView.FreezeRows(headerRow);
        sheet.Column(1).Width = 44;
        sheet.Column(2).Width = 16;
    }

    private static void WriteBreakdown(IXLWorksheet sheet, PayrollDocumentModel model, List<Payslip> payslips)
    {
        string[] headers = ["Emp no.", "Employee", "Item", "Amount (RM)"];

        sheet.Cell(1, 1).Value = $"{model.OrganizationName} — pay breakdown, {model.PeriodLabel}";
        sheet.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(13);
        sheet.Cell(2, 1).Value =
            "Base salary, overtime and every line item per employee. Deductions are negative; a benefit in kind is non-cash and not part of gross.";
        sheet.Cell(2, 1).Style.Font.SetItalic().Font.SetFontColor(Muted);

        const int headerRow = 4;
        for (var c = 0; c < headers.Length; c++)
            Heading(sheet.Cell(headerRow, c + 1), headers[c], Band.None);
        sheet.Cell(headerRow, 4).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

        var r = headerRow + 1;
        foreach (var p in payslips)
        {
            var items = model.LineItems.GetValueOrDefault(p.Id) ?? [];
            foreach (var line in PayrollSummaryPdf.Breakdown(p, items))
            {
                sheet.Cell(r, 1).SetValue(p.SnapshotEmployeeNumber ?? string.Empty);
                sheet.Cell(r, 2).Value = p.SnapshotName;
                sheet.Cell(r, 3).Value = line.Label;
                Money(sheet.Cell(r, 4), line.Amount, Band.None);
                if (line.Signed && line.Amount < 0m)
                    sheet.Cell(r, 4).Style.Font.SetFontColor(XLColor.FromHtml("#BE123C"));
                r++;
            }
        }

        sheet.Range(headerRow, 1, Math.Max(r - 1, headerRow), headers.Length).SetAutoFilter();
        sheet.SheetView.FreezeRows(headerRow);
        sheet.Column(1).Width = 11;
        sheet.Column(2).Width = 34;
        sheet.Column(3).Width = 44;
        sheet.Column(4).Width = 14;
    }

    // ─── Cells ──────────────────────────────────────────────────────────

    private static void Label(IXLCell cell, string text)
    {
        cell.Value = text;
        cell.Style.Font.SetBold().Font.SetFontColor(Muted);
    }

    private static void Heading(IXLCell cell, string text, Band band)
    {
        cell.Value = text;
        cell.Style.Font.SetBold();
        cell.Style.Fill.SetBackgroundColor(band switch
        {
            Band.Emp => EmpFill,
            Band.Er => ErFill,
            _ => HeadFill,
        });
        cell.Style.Border.SetBottomBorder(XLBorderStyleValues.Thin);
        cell.Style.Alignment.SetWrapText(true);
        cell.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
    }

    private static void BandCell(IXLWorksheet sheet, int row, Band band, string text)
    {
        var columns = Enumerable.Range(0, Amounts.Length)
            .Where(i => Amounts[i].Band == band)
            .Select(i => IdentityHeaders.Length + i + 1)
            .ToList();

        var range = sheet.Range(row, columns.Min(), row, columns.Max()).Merge();
        range.Value = text.ToUpperInvariant();
        range.Style.Font.SetBold().Font.SetFontColor(band == Band.Emp ? EmpInk : ErInk);
        range.Style.Fill.SetBackgroundColor(band == Band.Emp ? EmpFill : ErFill);
        range.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
    }

    private static void Money(IXLCell cell, decimal value, Band band)
    {
        cell.Value = value;
        cell.Style.NumberFormat.Format = MoneyFormat;
        if (band == Band.Emp) cell.Style.Fill.SetBackgroundColor(EmpFill);
        if (band == Band.Er) cell.Style.Fill.SetBackgroundColor(ErFill);
    }

    // Run timestamps are stored in UTC; the sheet shows Malaysian time, as the
    // generated stamp is.
    private static DateTime MalaysiaTime(DateTime utc) =>
        TimeZoneInfo.ConvertTimeBySystemTimeZoneId(
            DateTime.SpecifyKind(utc, DateTimeKind.Utc), Attendance.AttendanceTime.DefaultTimeZone);
}
