using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Modules.Payroll.Pdf;
using ClosedXML.Excel;

namespace AltomateHR.Api.Modules.Payroll;

// An approved run, written back out in Ayu Borneo's monthly timesheet layout —
// the sheet the ABPay companion app reads to post DRAFT runs here. It is that
// import in reverse.
//
// Sheet1 is ABPay's import layout EXACTLY: same headers, same order, the
// blank column O, typed values (no formulas), frozen header row. ABPay reads
// the FIRST sheet only (SheetJS `SheetNames[0]` in its timesheet.service), so
// the second sheet — the statutory figures per employee — cannot reach it.
//
// Columns come from the payslip snapshot and its line items, reversing
// ABPay's default column → category mapping (ABPay domain/posting.ts):
//
//   Basic       ProratedPay (ABPay sets the salary and runs with skipProration,
//               so ProratedPay = Basic for its runs; for any other run it is
//               what was actually paid)
//   U/L         − deduct_unpaid_leave        (negative, as the sheet writes it)
//   Travelling  allowance_travel_private
//   Meal        allowance_meal
//   Parking     allowance_parking
//   Hours       OT hours (normal + rest + public) the engine paid — ABPay's own
//               runs post OT as money only, so this is 0 for those
//   OT          OtPay + wages_overtime
//   Comm        wages_commission
//   Bonus       wages_bonus_non_annual
//   Deduction   − deduct_miscellaneous       (negative, as the sheet writes it)
//   Total Gross GrossPay + Deduction — the sheet's own arithmetic, where Total
//               Gross is every money column summed (E..N without Hours) and so
//               already net of the Deduction column. AltomateHR's gross keeps
//               a miscellaneous deduction out (it comes off net pay), hence
//               the adjustment.
//
// Anything else in gross — other allowance categories, attached claims, a
// salary adjustment or advance recovery — has no ABPay column. It is not
// dropped: Total Gross still includes it, so on that row Total Gross is more
// (or less) than the columns add up to, and the Statutory sheet names the
// difference in "Not in AB Pay columns". The Total Gross header carries a
// cell comment saying so, and the Statutory sheet ends in a Notes block.
public static class AbPayTimesheetXlsx
{
    public const string ContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    // Column O is blank in the real timesheet, header included. Kept, so the
    // file lines up with the ones ABPay is used to.
    private static readonly string[] Headers =
    [
        "Staff", "Company", "Group", "Outlet", "Basic", "U/L", "Travelling", "Meal",
        "Parking", "Hours", "OT", "Comm", "Bonus", "Deduction", "", "Total Gross",
    ];

    // The timesheet's own number format: Excel's built-in "Comma" style, which
    // shows zero as "-".
    private const string AccountingFormat =
        "_-* #,##0.00_-;\\-* #,##0.00_-;_-* \"-\"??_-;_-@_-";

    private const string MoneyFormat = "#,##0.00";

    public sealed record Row
    {
        public required string Staff { get; init; }
        public required string EmployeeNumber { get; init; }
        public required string Company { get; init; }
        public required string Group { get; init; }
        public required string Outlet { get; init; }

        public decimal Basic { get; init; }
        public decimal UnpaidLeave { get; init; }   // ≤ 0
        public decimal Travelling { get; init; }
        public decimal Meal { get; init; }
        public decimal Parking { get; init; }
        public decimal OtHours { get; init; }
        public decimal Ot { get; init; }
        public decimal Commission { get; init; }
        public decimal Bonus { get; init; }
        public decimal Deduction { get; init; }     // ≤ 0
        public decimal TotalGross { get; init; }

        // Money in Total Gross that no column carries. Zero when the row
        // reconciles the way the timesheet does.
        public decimal NotInColumns => Money.Round2(
            TotalGross - (Basic + UnpaidLeave + Travelling + Meal + Parking
                          + Ot + Commission + Bonus + Deduction));
    }

    // One row per payslip. AltomateHR does not split pay by outlet, so an
    // employee ABPay merged from several outlet rows comes back as one.
    public static IReadOnlyList<Row> BuildRows(PayrollDocumentModel model, string company) =>
        [.. model.Rows.Select(r => BuildRow(
            r, company, model.LineItems.GetValueOrDefault(r.Payslip.Id) ?? []))];

    public static Row BuildRow(
        StatutoryEmployeeRow row, string company, IReadOnlyList<PayslipLineItem> lineItems)
    {
        var p = row.Payslip;

        decimal Sum(string category) => lineItems
            .Where(li => string.Equals(li.Category, category, StringComparison.Ordinal))
            .Sum(li => li.Amount);

        var deduction = -Sum(PayrollAdjustmentCategories.DeductMiscellaneous);

        return new Row
        {
            Staff = p.SnapshotName,
            EmployeeNumber = row.EmployeeCode,
            Company = company,
            Group = row.Department?.Trim() ?? string.Empty,
            Outlet = row.Location?.Trim() ?? string.Empty,

            Basic = p.ProratedPay,
            UnpaidLeave = -Sum(PayrollAdjustmentCategories.DeductUnpaidLeave),
            Travelling = Sum(PayrollAdjustmentCategories.AllowanceTravelPrivate),
            Meal = Sum(PayrollAdjustmentCategories.AllowanceMeal),
            Parking = Sum(PayrollAdjustmentCategories.AllowanceParking),
            OtHours = p.OtNormalHours + p.OtRestHours + p.OtPublicHours,
            Ot = p.OtPay + Sum(PayrollAdjustmentCategories.WagesOvertime),
            Commission = Sum(PayrollAdjustmentCategories.WagesCommission),
            Bonus = Sum(PayrollAdjustmentCategories.WagesBonusNonAnnual),
            Deduction = deduction,
            TotalGross = Money.Round2(p.GrossPay + deduction),
        };
    }

    // PCB offsets the employee paid to a third party themselves — self-paid
    // zakat and the departure levy, declared on Borang TP1 (`OffsetsPcb` +
    // `CashNeutral`). PayslipCalculator counts them in Payslip.Zakat, because
    // they lower PCB exactly as payroll zakat does, but keeps them out of
    // TotalDeductions: nothing left this payslip. Read from the category
    // catalogue, the same flags the calculator routes on.
    public static decimal SelfPaidPcbOffsets(IReadOnlyList<PayslipLineItem> lineItems) =>
        Money.Round2(lineItems
            .Where(li => PayrollAdjustmentCategories.Find(li.Category) is { OffsetsPcb: true, CashNeutral: true })
            .Sum(li => li.Amount));

    // Zakat that actually came out of pay (and so sits inside TotalDeductions):
    // Payslip.Zakat less the self-paid offsets. Payslip.Zakat itself is left
    // as it is — PCB, EA and CP39 need the full offset.
    public static decimal ZakatFromPay(Payslip p, IReadOnlyList<PayslipLineItem> lineItems) =>
        Money.Round2(p.Zakat - SelfPaidPcbOffsets(lineItems));

    // The Statutory sheet's catch-all: every deduction off net pay that has no
    // column of its own (loan repayments, miscellaneous, CP38 …). Zakat from
    // pay and Additional PCB are inside TotalDeductions and shown in their own
    // columns, so they are netted off rather than counted twice. Never
    // negative for a generated payslip: both are subsets of TotalDeductions.
    public static decimal OtherDeductions(Payslip p, IReadOnlyList<PayslipLineItem> lineItems) =>
        Money.Round2(p.TotalDeductions - ZakatFromPay(p, lineItems) - p.VoluntaryPcb);

    // What goes in the Company column, and whether it is ABPay's own code.
    public sealed record Company(string Value, bool IsCode);

    // The AB Pay company code from Payroll Settings when one is set — that is
    // what ABPay matches the column against — else the organisation's name.
    public static Company CompanyColumn(string? abPayCompanyCode, string organizationName) =>
        string.IsNullOrWhiteSpace(abPayCompanyCode)
            ? new Company(organizationName.Trim(), IsCode: false)
            : new Company(abPayCompanyCode.Trim().ToUpperInvariant(), IsCode: true);

    // Comment on the Total Gross header. A note rather than different header
    // text: ABPay finds the column by its exact header ("total gross"), and
    // its parser reads cell values only, so a comment cannot affect a re-import.
    public const string TotalGrossNote =
        "Total Gross = Basic + U/L + Travelling + Meal + Parking + OT + Comm + Bonus + Deduction "
        + "(deductions are negative), the same rule as the ABPay timesheet. This differs from "
        + "AltomateHR's payroll gross, which does not subtract miscellaneous deductions — see the "
        + "Statutory sheet's Gross column.";

    // `selfPaidOffsets` is the run's total of SelfPaidPcbOffsets; the note
    // about them appears only when someone on the run has one.
    public static IReadOnlyList<string> Notes(Company company, decimal selfPaidOffsets = 0m)
    {
        var notes = NotesAlways(company).ToList();
        if (selfPaidOffsets != 0m)
        {
            notes.Add(
                "Self-paid zakat and departure levy declared on Borang TP1 (RM "
                + selfPaidOffsets.ToString("#,##0.00", System.Globalization.CultureInfo.InvariantCulture)
                + " in total) lowered PCB but were paid by the employee directly, not taken from pay, "
                + "so they are not in Zakat or Other deductions.");
        }
        return notes;
    }

    private static IReadOnlyList<string> NotesAlways(Company company) =>
    [
        "Total Gross (Sheet1) = Basic + U/L + Travelling + Meal + Parking + OT + Comm + Bonus + Deduction, "
            + "with U/L and Deduction negative — the ABPay timesheet's own rule. AltomateHR's Gross (this sheet) "
            + "does not subtract miscellaneous deductions, so it is higher by the Deduction amount.",
        "Not in AB Pay columns: pay with no ABPay column (e.g. phone allowance, annual bonus, claims, salary "
            + "adjustments). It is included in Sheet1's Total Gross, so that row's columns will not add up to "
            + "it, and ABPay would not import it if the sheet were re-imported.",
        "Loan repayments, CP38 and other deductions taken from net pay only are under Other deductions here, "
            + "not in Sheet1's Deduction column (which is miscellaneous deductions only).",
        company.IsCode
            ? $"Company (Sheet1) is the AB Pay company code set in Payroll Settings: {company.Value}."
            : "Company (Sheet1) is the company name, because no AB Pay company code is set in Payroll Settings. "
              + "Set one there for ABPay to recognise the file on re-import.",
        "PCB includes any Additional PCB (remitted in the same CP39 field), as on the payslip.",
    ];

    public static byte[] Render(PayrollDocumentModel model, Company company)
    {
        var rows = BuildRows(model, company.Value);

        using var workbook = new XLWorkbook();
        WriteTimesheet(workbook.AddWorksheet("Sheet1"), rows);
        WriteStatutory(workbook.AddWorksheet("Statutory"), model, rows, company);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteTimesheet(IXLWorksheet sheet, IReadOnlyList<Row> rows)
    {
        for (var c = 0; c < Headers.Length; c++)
        {
            var cell = sheet.Cell(1, c + 1);
            if (Headers[c].Length > 0) cell.Value = Headers[c];
            cell.Style.Font.SetBold();
        }

        // As in the timesheet: Staff's header is centred, wrapped and boxed
        // left and below; the rest are plain bold.
        var staffHeader = sheet.Cell(1, 1).Style;
        staffHeader.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
            .Alignment.SetVertical(XLAlignmentVerticalValues.Center)
            .Alignment.SetWrapText();
        staffHeader.Border.SetLeftBorder(XLBorderStyleValues.Medium)
            .Border.SetBottomBorder(XLBorderStyleValues.Medium);
        sheet.Row(1).Height = 15.75;

        var note = sheet.Cell(1, 16).CreateComment();
        note.Author = "AltomateHR";
        note.AddText(TotalGrossNote);
        note.Style.Size.SetWidth(40).Size.SetHeight(9);

        var r = 2;
        foreach (var row in rows)
        {
            sheet.Cell(r, 1).Value = row.Staff;
            sheet.Cell(r, 2).Value = row.Company;
            sheet.Cell(r, 3).Value = row.Group;
            sheet.Cell(r, 4).Value = row.Outlet;

            // Typed values, zeros included — the timesheet writes 0, not blank.
            decimal[] money =
            [
                row.Basic, row.UnpaidLeave, row.Travelling, row.Meal, row.Parking,
                row.OtHours, row.Ot, row.Commission, row.Bonus, row.Deduction,
            ];
            for (var i = 0; i < money.Length; i++) sheet.Cell(r, 5 + i).Value = money[i];
            sheet.Cell(r, 16).Value = row.TotalGross;

            r++;
        }

        if (rows.Count > 0)
            sheet.Range(2, 5, r - 1, 16).Style.NumberFormat.Format = AccountingFormat;

        sheet.Columns(1, 4).AdjustToContents(1, Math.Max(1, r - 1));
        sheet.Column(1).Width = Math.Max(sheet.Column(1).Width, 10);
        sheet.Columns(5, 16).Width = 10.5;
        sheet.Column(15).Width = 4;
        sheet.SheetView.FreezeRows(1);
    }

    private static void WriteStatutory(
        IXLWorksheet sheet, PayrollDocumentModel model, IReadOnlyList<Row> rows, Company company)
    {
        var payslips = model.Rows.Select(r => r.Payslip).ToList();
        var lineItems = payslips
            .Select(p => model.LineItems.GetValueOrDefault(p.Id) ?? [])
            .ToList();

        // Optional columns appear only when someone on the run has the figure,
        // so a company without zakat does not carry a column of dashes.
        // Everything that comes off gross is shown, so each row reconciles:
        // Gross − employee EPF/SOCSO/EIS/SKBBK − PCB − Zakat − Other = Net Pay.
        var columns = new List<(string Header, Func<int, decimal> Value)>
        {
            ("Gross", i => payslips[i].GrossPay),
            ("EPF (Employee)", i => payslips[i].EpfEmployee),
            ("EPF (Employer)", i => payslips[i].EpfEmployer),
            ("SOCSO (Employee)", i => payslips[i].SocsoEmployee),
            ("SOCSO (Employer)", i => payslips[i].SocsoEmployer),
            ("EIS (Employee)", i => payslips[i].EisEmployee),
            ("EIS (Employer)", i => payslips[i].EisEmployer),
        };
        if (payslips.Any(p => p.SkbbkEmployee != 0m))
            columns.Add(("SKBBK (Employee)", i => payslips[i].SkbbkEmployee));

        // Additional PCB is remitted in the same CP39 field, so it is shown in
        // PCB — as on the payslip.
        columns.Add(("PCB", i => payslips[i].Pcb + payslips[i].VoluntaryPcb));

        // Zakat taken from pay only. Self-paid (TP1) zakat and departure levy
        // are in Payslip.Zakat but never left the payslip — see ZakatFromPay.
        decimal Zakat(int i) => ZakatFromPay(payslips[i], lineItems[i]);
        if (Enumerable.Range(0, payslips.Count).Any(i => Zakat(i) != 0m))
            columns.Add(("Zakat", Zakat));

        // The payslip's own catch-all: every other deduction off net pay
        // (loan repayments, miscellaneous, CP38 …).
        decimal Other(int i) => OtherDeductions(payslips[i], lineItems[i]);
        if (Enumerable.Range(0, payslips.Count).Any(i => Other(i) != 0m))
            columns.Add(("Other deductions", Other));

        columns.Add(("Net Pay", i => payslips[i].NetPay));

        if (rows.Any(r => r.NotInColumns != 0m))
            columns.Add(("Not in AB Pay columns", i => rows[i].NotInColumns));

        sheet.Cell(1, 1).Value = "Staff";
        sheet.Cell(1, 2).Value = "Employee No.";
        for (var c = 0; c < columns.Count; c++) sheet.Cell(1, 3 + c).Value = columns[c].Header;
        var lastColumn = 2 + columns.Count;
        sheet.Range(1, 1, 1, lastColumn).Style.Font.SetBold()
            .Border.SetBottomBorder(XLBorderStyleValues.Thin);

        var r = 2;
        for (var i = 0; i < payslips.Count; i++, r++)
        {
            sheet.Cell(r, 1).Value = rows[i].Staff;
            // Text, so a number like "00077" keeps its zeros.
            sheet.Cell(r, 2).SetValue(rows[i].EmployeeNumber);
            for (var c = 0; c < columns.Count; c++) sheet.Cell(r, 3 + c).Value = columns[c].Value(i);
        }

        // Totals as typed figures, like the rest of the file.
        sheet.Cell(r, 1).Value = $"Total ({payslips.Count})";
        for (var c = 0; c < columns.Count; c++)
        {
            var value = columns[c].Value;
            sheet.Cell(r, 3 + c).Value = Enumerable.Range(0, payslips.Count).Sum(value);
        }
        sheet.Range(r, 1, r, lastColumn).Style.Font.SetBold()
            .Border.SetTopBorder(XLBorderStyleValues.Thin);
        sheet.Range(2, 3, r, lastColumn).Style.NumberFormat.Format = MoneyFormat;

        // Notes, two rows under the totals. Each sits in column A and runs
        // across the empty cells to its right.
        r += 2;
        sheet.Cell(r, 1).Value = "Notes";
        sheet.Cell(r, 1).Style.Font.SetBold();
        r++;
        sheet.Cell(r, 1).Value = $"{model.OrganizationName} — {model.PeriodLabel}, from the approved payroll run.";
        sheet.Cell(r, 1).Style.Font.SetFontColor(XLColor.Gray);
        r++;
        var selfPaid = lineItems.Sum(SelfPaidPcbOffsets);
        foreach (var note in Notes(company, selfPaid))
        {
            sheet.Cell(r, 1).Value = "• " + note;
            sheet.Cell(r, 1).Style.Font.SetFontColor(XLColor.Gray);
            r++;
        }

        sheet.Columns(1, lastColumn).AdjustToContents(1, 2 + payslips.Count);
        sheet.SheetView.FreezeRows(1);
    }
}
