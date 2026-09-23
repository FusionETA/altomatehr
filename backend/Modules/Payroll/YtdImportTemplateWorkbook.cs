using ClosedXML.Excel;

namespace AltomateHR.Api.Modules.Payroll;

// The YTD history template, laid out exactly as the reference system lays it
// out — three sheets, the header on row 5, and the reference system's own
// column names and order.
//
// A bespoke builder rather than a TabularSheet, because that layer writes one
// flat sheet and this file is three, with merged colour bands, per-employee
// SUM formulas and frozen panes. Matching the reference matters more than
// sharing the writer: an admin migrating off it arrives holding a workbook in
// THIS shape, and the two have to be interchangeable in both directions.
//
// The parser reads the header by name and takes the first sheet that has one,
// so the instructions sheet in front of the data is not in its way.
public static class YtdImportTemplateWorkbook
{
    private static readonly XLColor BrandPurple = XLColor.FromHtml("#5B21B6");
    private static readonly XLColor BrandPurpleLight = XLColor.FromHtml("#E9D5FF");
    private static readonly XLColor BrandPurpleFaint = XLColor.FromHtml("#F5F3FF");
    private static readonly XLColor SampleGreen = XLColor.FromHtml("#22C55E");
    private static readonly XLColor HeaderText = XLColor.FromHtml("#FFFFFF");
    private static readonly XLColor MonthText = XLColor.FromHtml("#6B7280");

    private const string NumFormat = "#,##0.00;(#,##0.00);-";

    // The reference system's column set and order. The parser matches on the
    // label, so order is cosmetic to it — but an admin comparing the two files
    // side by side should not have to hunt.
    private static readonly string[] MandatoryColumns =
    [
        "Full Name", "Personal ID", "Basic Salary", "PCB",
        "Employee EPF", "Employee SOCSO", "Employee EIS",
        "Employer EPF", "Employer SOCSO", "Employer EIS", "HRDF",
    ];

    private static readonly string[] OptionalColumns =
    [
        "Bonus", "Commission", "Overtime", "Service Charge",
        "Travel/Petrol Allowance", "Parking Allowance",
        "Phone/Broadband Allowance", "Other Allowance",
        "Unpaid Leave", "Net Salary Deduction", "Zakat",
        // Skim LINDUNG 24 Jam runs from 1 Jun 2026. Blank before then; a
        // previous system's June-onwards history does carry it.
        "Employee SKBBK",
    ];

    public sealed record Employee(string Name, string PersonalIdLabel);

    public static byte[] Build(int year, string organizationName, IReadOnlyList<Employee> employees)
    {
        using var wb = new XLWorkbook();

        BuildInstructions(wb, year);
        BuildDataSheet(wb, $"✏️ YTD Data {year}", $"✏️ YTD Data {year}",
            organizationName, employees, prefilled: false, tabColor: BrandPurple);
        BuildDataSheet(wb, "\U0001F4D7 YTD Data — Sample", "\U0001F4D7 Sample YTD Data — reference only",
            "Sample Sdn. Bhd.", [new Employee("Ali bin Ahmad", "NRIC: 901231-12-3456")],
            prefilled: true, tabColor: SampleGreen);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static void BuildInstructions(XLWorkbook wb, int year)
    {
        var ws = wb.Worksheets.Add("\U0001F4D5 Instructions");
        ws.SetTabColor(BrandPurple);
        ws.Column(1).Width = 2;
        ws.Column(2).Width = 28;
        ws.Column(3).Width = 70;

        ws.Range(2, 2, 2, 3).Merge();
        var title = ws.Cell(2, 2);
        title.Value = "\U0001F4D5 YTD payroll-history import — Instructions";
        title.Style.Font.SetBold().Font.SetFontSize(14).Font.SetFontColor(HeaderText);
        title.Style.Fill.SetBackgroundColor(BrandPurple);
        title.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center).Alignment.SetIndent(1);
        ws.Row(2).Height = 28;

        (string Heading, string[] Body)[] sections =
        [
            ("What this file is for", [
                "Use this template when you start using AltomateHR mid-year and need to bring in payroll history from your previous system. The PCB engine uses each employee's year-to-date totals to calculate the right tax for the rest of the year — without this, the first run after migration will under- or over-deduct.",
            ]),
            ("How to fill it", [
                $"Open the \"✏️ YTD Data {year}\" sheet — your employees are already listed.",
                "For each employee, fill the 12 month rows below the header row with what was actually paid that month. Leave a month blank or 0 if no payroll happened that month (e.g. employee joined in March → Jan and Feb stay 0).",
                "The header row's YTD totals are for your reference — the importer recomputes them from the month rows.",
            ]),
            ("Mandatory columns", [
                "Basic Salary, PCB, Employee EPF, Employee SOCSO, Employee EIS, Employer EPF, Employer SOCSO, Employer EIS, HRDF.",
                "Leave 0 if not applicable (e.g. HRDF = 0 for foreign workers; EIS = 0 if employee is over 60).",
            ]),
            ("Optional columns", [
                "Each optional column header must match one of the supported labels exactly (case-insensitive). Add columns for the categories you actually paid; delete the ones you don't.",
                "Quick-pick (the columns this template starts with):",
                "  • Bonus · Commission · Overtime · Service Charge",
                "  • Travel/Petrol Allowance (also: \"Travel Allowance\", \"Petrol Allowance\")",
                "  • Parking Allowance",
                "  • Phone/Broadband Allowance (also: \"Phone Allowance\", \"Broadband Allowance\")",
                "  • Other Allowance · Unpaid Leave · Net Salary Deduction · Zakat",
                "  • Employee SKBBK (Skim LINDUNG 24 Jam — Jun 2026 onwards; leave blank for earlier months)",
                "Any adjustment-category label the per-run adjustment form offers also works — for example \"Living Accommodation\", \"Travel/Petrol/Toll (Official Duty)\" or \"Unpaid Leave deduction\". Each label routes the amount through that category's statutory rules: a benefit in kind lands as non-cash, a CP38 column flows to the PCB-deduction bucket.",
            ]),
            ("Conflict handling", [
                "On upload: any month that already has a submitted payroll run in AltomateHR for that employee is SKIPPED (we won't overwrite real run history).",
                "Employees whose NRIC / Passport doesn't match an existing AltomateHR employee are SKIPPED — the upload summary lists them so you can add them and re-upload.",
            ]),
            ("Format rules", [
                "Amounts are in Malaysian Ringgit, 2 decimal places. Use 0 not blank.",
                "Personal ID format: NRIC: 001127-08-0576 or Passport: A1234567 — keep the prefix.",
                "Don't delete or rename the mandatory columns. Optional columns can be deleted if unused.",
            ]),
        ];

        var r = 4;
        foreach (var (heading, body) in sections)
        {
            var head = ws.Cell(r, 2);
            head.Value = heading;
            head.Style.Font.SetBold().Font.SetFontSize(12).Font.SetFontColor(BrandPurple);
            r++;
            foreach (var line in body)
            {
                ws.Range(r, 2, r, 3).Merge();
                var cell = ws.Cell(r, 2);
                cell.Value = line;
                cell.Style.Alignment.SetWrapText(true)
                    .Alignment.SetVertical(XLAlignmentVerticalValues.Top);
                cell.Style.Font.SetFontSize(11);
                ws.Row(r).Height = Math.Max(20, (int)Math.Ceiling(line.Length / 80.0) * 18);
                r++;
            }
            r++;
        }
    }

    private static void BuildDataSheet(
        XLWorkbook wb, string sheetName, string title, string subtitle,
        IReadOnlyList<Employee> employees, bool prefilled, XLColor tabColor)
    {
        var ws = wb.Worksheets.Add(sheetName);
        ws.SetTabColor(tabColor);

        var all = MandatoryColumns.Concat(OptionalColumns).ToArray();
        var totalCols = all.Length;

        ws.Column(1).Width = 26;
        ws.Column(2).Width = 24;
        for (var c = 3; c <= totalCols; c++) ws.Column(c).Width = 16;

        // Row 1 — title band
        ws.Range(1, 1, 1, totalCols).Merge();
        var t = ws.Cell(1, 1);
        t.Value = title;
        t.Style.Font.SetBold().Font.SetFontSize(14).Font.SetFontColor(HeaderText);
        t.Style.Fill.SetBackgroundColor(BrandPurple);
        t.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center).Alignment.SetIndent(1);
        ws.Row(1).Height = 28;

        // Row 2 — subtitle
        ws.Range(2, 1, 2, totalCols).Merge();
        var st = ws.Cell(2, 1);
        st.Value = subtitle;
        st.Style.Font.SetItalic().Font.SetFontColor(BrandPurple);
        st.Style.Fill.SetBackgroundColor(BrandPurpleFaint);
        st.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center).Alignment.SetIndent(1);
        ws.Row(2).Height = 20;

        ws.Row(3).Height = 8;

        // Row 4 — the MANDATORY / OPTIONAL bands
        var mandatoryEnd = MandatoryColumns.Length;
        ws.Range(4, 3, 4, mandatoryEnd).Merge();
        var band = ws.Cell(4, 3);
        band.Value = "MANDATORY — do not remove or rename";
        band.Style.Font.SetBold().Font.SetFontSize(11).Font.SetFontColor(HeaderText);
        band.Style.Fill.SetBackgroundColor(BrandPurple);
        band.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

        ws.Range(4, mandatoryEnd + 1, 4, totalCols).Merge();
        var opt = ws.Cell(4, mandatoryEnd + 1);
        opt.Value = "OPTIONAL — delete unused columns · don't rename or invent new ones · order doesn't matter";
        opt.Style.Font.SetBold().Font.SetFontSize(11).Font.SetFontColor(BrandPurple);
        opt.Style.Fill.SetBackgroundColor(BrandPurpleLight);
        opt.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        ws.Row(4).Height = 22;

        // Row 5 — the header the parser reads
        for (var c = 1; c <= totalCols; c++)
        {
            var cell = ws.Cell(5, c);
            cell.Value = all[c - 1];
            cell.Style.Font.SetBold().Font.SetFontColor(HeaderText);
            cell.Style.Fill.SetBackgroundColor(BrandPurple);
            cell.Style.Alignment.SetWrapText(true)
                .Alignment.SetVertical(XLAlignmentVerticalValues.Center);
        }
        ws.Row(5).Height = 30;
        ws.SheetView.Freeze(5, 2);

        var row = 6;
        foreach (var emp in employees) row = WriteEmployee(ws, emp, row, totalCols, prefilled);
    }

    // One employee: a header row carrying the name, id and a SUM per column,
    // then the twelve month rows underneath it.
    private static int WriteEmployee(
        IXLWorksheet ws, Employee emp, int startRow, int totalCols, bool prefilled)
    {
        var header = ws.Row(startRow);
        ws.Cell(startRow, 1).Value = emp.Name;
        ws.Cell(startRow, 1).Style.Font.SetBold().Font.SetFontColor(BrandPurple);
        ws.Cell(startRow, 2).Value = emp.PersonalIdLabel;
        ws.Cell(startRow, 2).Style.Font.SetFontColor(BrandPurple);

        for (var c = 3; c <= totalCols; c++)
        {
            var letter = ws.Column(c).ColumnLetter();
            var cell = ws.Cell(startRow, c);
            cell.FormulaA1 = $"SUM({letter}{startRow + 1}:{letter}{startRow + 12})";
            cell.Style.NumberFormat.Format = NumFormat;
            cell.Style.Font.SetBold();
        }

        for (var c = 1; c <= totalCols; c++)
        {
            var cell = ws.Cell(startRow, c);
            cell.Style.Fill.SetBackgroundColor(BrandPurpleFaint);
            cell.Style.Border.SetTopBorder(XLBorderStyleValues.Medium)
                .Border.SetTopBorderColor(BrandPurple)
                .Border.SetBottomBorder(XLBorderStyleValues.Thin)
                .Border.SetBottomBorderColor(BrandPurpleLight);
        }
        header.Height = 22;

        for (var m = 0; m < 12; m++)
        {
            var r = startRow + 1 + m;
            var month = ws.Cell(r, 1);
            month.Value = YtdImportParser.MonthLabels()[m];
            month.Style.Font.SetItalic().Font.SetFontColor(MonthText);

            for (var c = 3; c <= totalCols; c++)
            {
                var cell = ws.Cell(r, c);
                var sample = prefilled ? SampleValue(m, c) : null;
                if (sample is not null) cell.Value = sample.Value;
                cell.Style.NumberFormat.Format = NumFormat;
            }
            ws.Row(r).Height = 18;
        }

        return startRow + 13;
    }

    // The sample sheet tells the mid-year-cutover story: nothing until May,
    // then a steady month, so an admin sees a filled row and an empty one.
    private static decimal? SampleValue(int monthIndex, int column)
    {
        if (monthIndex < 4) return 0m;

        return column switch
        {
            3 => 5000m,                                   // Basic Salary
            4 => monthIndex == 11 ? 245.60m : 124.30m,    // PCB
            5 => 715m,                                    // Employee EPF
            6 => 24.75m,                                  // Employee SOCSO
            7 => 9.90m,                                   // Employee EIS
            8 => 785m,                                    // Employer EPF
            9 => 86.65m,                                  // Employer SOCSO
            10 => 9.90m,                                  // Employer EIS
            11 => 50m,                                    // HRDF
            _ => null,
        };
    }
}
