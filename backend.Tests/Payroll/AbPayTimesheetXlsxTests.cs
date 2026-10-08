using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Modules.Payroll.Pdf;
using ClosedXML.Excel;

namespace AltomateHR.Api.Tests.Payroll;

// The AB Pay timesheet export: an approved run written back out in the layout
// the ABPay companion app imports. What is pinned is what ABPay's parser reads
// (header text and order, first sheet, typed numbers, sign conventions) and
// that no money goes missing between gross and the columns.
public class AbPayTimesheetXlsxTests
{
    private static PayslipLineItem Line(string category, decimal amount, PayslipLineKind kind) => new()
    {
        PayslipId = "ps-1",
        Category = category,
        Amount = amount,
        Kind = kind,
    };

    // Gross follows PayslipCalculator: basic + OT + allowances − unpaid leave.
    // The miscellaneous deduction comes off net, not gross.
    private static (StatutoryEmployeeRow Row, List<PayslipLineItem> Lines) Employee(
        decimal extraAllowance = 0m)
    {
        var lines = new List<PayslipLineItem>
        {
            Line(PayrollAdjustmentCategories.AllowanceTravelPrivate, 300m, PayslipLineKind.ALLOWANCE),
            Line(PayrollAdjustmentCategories.AllowanceMeal, 250m, PayslipLineKind.ALLOWANCE),
            Line(PayrollAdjustmentCategories.AllowanceParking, 150m, PayslipLineKind.ALLOWANCE),
            Line(PayrollAdjustmentCategories.WagesOvertime, 553.85m, PayslipLineKind.ALLOWANCE),
            Line(PayrollAdjustmentCategories.WagesCommission, 2655.15m, PayslipLineKind.ALLOWANCE),
            Line(PayrollAdjustmentCategories.WagesBonusNonAnnual, 500m, PayslipLineKind.ALLOWANCE),
            Line(PayrollAdjustmentCategories.DeductUnpaidLeave, 69.23m, PayslipLineKind.DEDUCTION),
            Line(PayrollAdjustmentCategories.DeductMiscellaneous, 800m, PayslipLineKind.DEDUCTION),
        };
        if (extraAllowance != 0m)
            lines.Add(Line(PayrollAdjustmentCategories.AllowancePhoneFixed, extraAllowance, PayslipLineKind.ALLOWANCE));

        var gross = 2000m + 300m + 250m + 150m + 553.85m + 2655.15m + 500m + extraAllowance - 69.23m;
        var payslip = new Payslip
        {
            Id = "ps-1",
            SnapshotName = "RUBIAH BINTI KATIM",
            SnapshotEmployeeNumber = "00027",
            BasicPay = 2000m,
            ProratedPay = 2000m,
            GrossPay = gross,
            EpfEmployee = 220m, EpfEmployer = 260m,
            SocsoEmployee = 10m, SocsoEmployer = 35m,
            EisEmployee = 4m, EisEmployer = 4m,
            Pcb = 100m,
            TotalDeductions = 800m,
            NetPay = gross - 220m - 10m - 4m - 100m - 800m,
        };

        return (new StatutoryEmployeeRow
        {
            Payslip = payslip,
            EmployeeName = "Rubiah Binti Katim",
            EmployeeCode = "00027",
            Department = "OUTLET",
            Location = "HQM",
        }, lines);
    }

    private static PayrollDocumentModel Model(StatutoryEmployeeRow row, List<PayslipLineItem> lines) => new()
    {
        Run = new PayrollRun { Id = "run-1", PeriodYear = 2026, PeriodMonth = 8, Status = PayrollRunStatus.SUBMITTED },
        OrganizationName = "Ayu Borneo (Management) Sdn Bhd",
        PeriodLabel = "August 2026",
        StatusLabel = "Submitted",
        IssueDate = new DateTime(2026, 8, 31),
        Rows = [row],
        LineItems = new Dictionary<string, IReadOnlyList<PayslipLineItem>> { ["ps-1"] = lines },
    };

    [Fact]
    public void Columns_ReverseTheAbPayMapping_WithTheSheetsSigns()
    {
        var (row, lines) = Employee();

        var r = AbPayTimesheetXlsx.BuildRow(row, "ABM", lines);

        Assert.Equal("RUBIAH BINTI KATIM", r.Staff);   // the snapshot name, no code prefix
        Assert.Equal("OUTLET", r.Group);
        Assert.Equal("HQM", r.Outlet);
        Assert.Equal(2000m, r.Basic);
        Assert.Equal(-69.23m, r.UnpaidLeave);
        Assert.Equal(300m, r.Travelling);
        Assert.Equal(250m, r.Meal);
        Assert.Equal(150m, r.Parking);
        Assert.Equal(553.85m, r.Ot);
        Assert.Equal(2655.15m, r.Commission);
        Assert.Equal(500m, r.Bonus);
        Assert.Equal(-800m, r.Deduction);
    }

    // The timesheet's Total Gross is its money columns summed, Deduction
    // included. When every ringgit maps to a column, the export agrees.
    [Fact]
    public void TotalGross_IsTheColumnsSummed_WhenEverythingMaps()
    {
        var (row, lines) = Employee();

        var r = AbPayTimesheetXlsx.BuildRow(row, "ABM", lines);

        Assert.Equal(row.Payslip.GrossPay - 800m, r.TotalGross);
        Assert.Equal(0m, r.NotInColumns);
    }

    // A phone allowance has no AB Pay column. It stays in Total Gross and is
    // named, rather than vanishing from the file.
    [Fact]
    public void UnmappedPay_StaysInTotalGross_AndIsNamed()
    {
        var (row, lines) = Employee(extraAllowance: 120m);

        var r = AbPayTimesheetXlsx.BuildRow(row, "ABM", lines);

        Assert.Equal(row.Payslip.GrossPay - 800m, r.TotalGross);
        Assert.Equal(120m, r.NotInColumns);
    }

    [Fact]
    public void Sheet1_IsTheAbPayImportLayout()
    {
        var (row, lines) = Employee(extraAllowance: 120m);

        var bytes = AbPayTimesheetXlsx.Render(
            Model(row, lines), AbPayTimesheetXlsx.CompanyColumn(null, "Ayu Borneo (Management)"));

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet(1);
        Assert.Equal("Sheet1", sheet.Name);

        string[] expected =
        [
            "Staff", "Company", "Group", "Outlet", "Basic", "U/L", "Travelling", "Meal",
            "Parking", "Hours", "OT", "Comm", "Bonus", "Deduction", "", "Total Gross",
        ];
        Assert.Equal(expected, Enumerable.Range(1, 16).Select(c => sheet.Cell(1, c).GetString()));
        Assert.Equal(1, sheet.SheetView.SplitRow);

        // The Total Gross rule is a NOTE on the header, never in the header
        // text — ABPay finds the column by its exact name.
        var totalGross = sheet.Cell(1, 16);
        Assert.Equal("Total Gross", totalGross.GetString());
        Assert.True(totalGross.HasComment);
        Assert.Equal(AbPayTimesheetXlsx.TotalGrossNote, totalGross.GetComment().Text);

        // Typed numbers, not formulas or text — ABPay reads raw values.
        Assert.Equal(XLDataType.Number, sheet.Cell(2, 5).DataType);
        Assert.False(sheet.Cell(2, 16).HasFormula);
        Assert.Equal("Ayu Borneo (Management)", sheet.Cell(2, 2).GetString());
        Assert.Equal(-800m, sheet.Cell(2, 14).GetValue<decimal>());

        // The second sheet reconciles to net pay and names the unmapped pay.
        var stat = workbook.Worksheet("Statutory");
        var headers = Enumerable.Range(1, 20).Select(c => stat.Cell(1, c).GetString()).ToList();
        Assert.Contains("Net Pay", headers);
        Assert.Contains("Other deductions", headers);
        Assert.Contains("Not in AB Pay columns", headers);
        Assert.DoesNotContain("Zakat", headers);   // nobody has zakat
        Assert.Equal("00027", stat.Cell(2, 2).GetString());
        Assert.Equal(120m, stat.Cell(2, headers.IndexOf("Not in AB Pay columns") + 1).GetValue<decimal>());
    }

    // The code from Payroll Settings wins, normalised the way ABPay matches
    // it; without one the column falls back to the organisation's name.
    [Theory]
    [InlineData(" abm ", "ABM", true)]
    [InlineData(null, "Ayu Borneo (Management)", false)]
    [InlineData("  ", "Ayu Borneo (Management)", false)]
    public void Company_UsesTheAbPayCode_ElseTheOrganisationName(string? code, string expected, bool isCode)
    {
        var company = AbPayTimesheetXlsx.CompanyColumn(code, "Ayu Borneo (Management)");

        Assert.Equal(expected, company.Value);
        Assert.Equal(isCode, company.IsCode);
    }

    [Theory]
    [InlineData("ABM")]
    [InlineData(null)]
    public void StatutorySheet_EndsInANotesBlock_UnderTheTotals(string? code)
    {
        var (row, lines) = Employee(extraAllowance: 120m);
        var company = AbPayTimesheetXlsx.CompanyColumn(code, "Ayu Borneo (Management)");

        using var workbook = new XLWorkbook(new MemoryStream(
            AbPayTimesheetXlsx.Render(Model(row, lines), company)));
        var sheet = workbook.Worksheet(1);
        var stat = workbook.Worksheet("Statutory");

        // Sheet1's Company column is the chosen value.
        Assert.Equal(company.Value, sheet.Cell(2, 2).GetString());

        // One employee: header, row 2, totals on row 3, a blank row, then Notes.
        Assert.StartsWith("Total (1)", stat.Cell(3, 1).GetString());
        Assert.Equal("", stat.Cell(4, 1).GetString());
        Assert.Equal("Notes", stat.Cell(5, 1).GetString());

        var notes = Enumerable.Range(6, 10).Select(r => stat.Cell(r, 1).GetString()).ToList();
        Assert.Contains(notes, n => n.Contains("Total Gross (Sheet1) = Basic + U/L"));
        Assert.Contains(notes, n => n.Contains("Not in AB Pay columns"));
        Assert.Contains(notes, n => n.Contains("Other deductions"));
        Assert.Contains(notes, n => code is null
            ? n.Contains("no AB Pay company code is set")
            : n.Contains("AB Pay company code set in Payroll Settings: ABM"));
    }
}
