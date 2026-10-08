using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Modules.Payroll.Pdf;
using AltomateHR.Api.Modules.Policies.Entities;
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

        var bytes = AbPayTimesheetXlsx.Render(Model(row, lines), "Ayu Borneo (Management)");

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

    // Company is always the organisation's full name (trimmed); there is no
    // AB Pay code setting. The Notes say how to swap in the code for a re-import.
    [Fact]
    public void StatutorySheet_EndsInANotesBlock_UnderTheTotals()
    {
        var (row, lines) = Employee(extraAllowance: 120m);

        using var workbook = new XLWorkbook(new MemoryStream(
            AbPayTimesheetXlsx.Render(Model(row, lines), "  Ayu Borneo (Management) ")));
        var sheet = workbook.Worksheet(1);
        var stat = workbook.Worksheet("Statutory");

        // Sheet1's Company column is the company name.
        Assert.Equal("Ayu Borneo (Management)", sheet.Cell(2, 2).GetString());

        // One employee: header, row 2, totals on row 3, a blank row, then Notes.
        Assert.StartsWith("Total (1)", stat.Cell(3, 1).GetString());
        Assert.Equal("", stat.Cell(4, 1).GetString());
        Assert.Equal("Notes", stat.Cell(5, 1).GetString());

        var notes = Enumerable.Range(6, 10).Select(r => stat.Cell(r, 1).GetString()).ToList();
        Assert.Contains(notes, n => n.Contains("Total Gross (Sheet1) = Basic + U/L"));
        Assert.Contains(notes, n => n.Contains("Not in AB Pay columns"));
        Assert.Contains(notes, n => n.Contains("Other deductions"));
        Assert.Contains(notes, n => n.Contains("Company (Sheet1) is the company name")
                                    && n.Contains("find and replace"));
        Assert.DoesNotContain(notes, n => n.Contains("Payroll Settings"));
    }

    // ─── Self-paid (TP1) PCB offsets on the Statutory sheet ─────────────
    //
    // Self-paid zakat and the departure levy are in Payslip.Zakat (they lower
    // PCB) but not in TotalDeductions (nothing left the payslip). The sheet
    // must show only zakat taken from pay, keep Other deductions >= 0, and
    // still reconcile every row to Net Pay. The payslips come from the real
    // PayslipCalculator so the tests follow its semantics, not a hand model.

    private static (StatutoryEmployeeRow Row, List<PayslipLineItem> Lines, PayslipCalculator.Result Calc)
        Calculated(params (string Category, decimal Amount)[] rows)
    {
        var calc = PayslipCalculator.Calculate(new PayslipCalculator.Input
        {
            PeriodYear = 2026,
            PeriodMonth = 1,
            SalaryType = SalaryType.MONTHLY,
            MonthlySalary = 9000m,
            FixedAllowances = [.. rows.Select(r => new FixedAllowance
            {
                Category = r.Category, Name = null, Amount = r.Amount, TreatAsRecurring = false,
            })],
            Nationality = "Malaysian",
            DateOfBirth = new DateTime(1990, 6, 15),
            IsResident = true,
            EpfEmployeeRate = 11m,
            SocsoScheme = SocsoScheme.EMPLOYMENT_INJURY_INVALIDITY,
            ContributeToEis = true,
            IncomeTaxNumber = "SG12345678",
            EpfNumber = "1234567",
            SocsoNumber = "900615012345",
            WorkingDaysRule = WorkingDaysRule.TWENTY_SIX,
            YtdAllowanceByCategory = new Dictionary<string, decimal>(),
        });

        var payslip = new Payslip
        {
            Id = "ps-1",
            SnapshotName = "RUBIAH BINTI KATIM",
            SnapshotEmployeeNumber = "00027",
            BasicPay = calc.BasicPay,
            ProratedPay = calc.ProratedPay,
            OtPay = calc.OtPay,
            TotalAllowances = calc.TotalAllowances,
            TotalReimbursements = calc.TotalReimbursements,
            TotalDeductions = calc.TotalDeductions,
            GrossPay = calc.GrossPay,
            EpfEmployee = calc.EpfEmployee, EpfEmployer = calc.EpfEmployer,
            SocsoEmployee = calc.SocsoEmployee, SocsoEmployer = calc.SocsoEmployer,
            EisEmployee = calc.EisEmployee, EisEmployer = calc.EisEmployer,
            SkbbkEmployee = calc.SkbbkEmployee,
            Pcb = calc.Pcb,
            VoluntaryPcb = calc.VoluntaryPcb,
            Cp38 = calc.Cp38,
            Zakat = calc.Zakat,
            NetPay = calc.NetPay,
        };
        var lines = calc.LineItems.Select(li => new PayslipLineItem
        {
            PayslipId = "ps-1",
            Category = li.Category,
            Label = li.Label,
            Amount = li.Amount,
            Kind = li.Kind,
        }).ToList();

        return (new StatutoryEmployeeRow
        {
            Payslip = payslip,
            EmployeeName = "Rubiah Binti Katim",
            EmployeeCode = "00027",
            Department = "OUTLET",
            Location = "HQM",
        }, lines, calc);
    }

    // The rendered Statutory sheet's first data row, by header, and the notes.
    private static (Dictionary<string, decimal> Cells, List<string> Notes) StatutoryRow(
        StatutoryEmployeeRow row, List<PayslipLineItem> lines)
    {
        using var workbook = new XLWorkbook(new MemoryStream(AbPayTimesheetXlsx.Render(
            Model(row, lines), "Ayu Borneo (Management)")));
        var stat = workbook.Worksheet("Statutory");

        var cells = new Dictionary<string, decimal>();
        for (var c = 3; stat.Cell(1, c).GetString() is { Length: > 0 } header; c++)
            cells[header] = stat.Cell(2, c).GetValue<decimal>();

        var notes = Enumerable.Range(5, 15).Select(r => stat.Cell(r, 1).GetString()).ToList();
        return (cells, notes);
    }

    // Gross less every employee-side column = Net Pay, to the sen.
    private static void AssertReconciles(Dictionary<string, decimal> cells)
    {
        var taken = cells
            .Where(kv => kv.Key.EndsWith("(Employee)", StringComparison.Ordinal)
                         || kv.Key is "PCB" or "Zakat" or "Other deductions")
            .Sum(kv => kv.Value);
        Assert.Equal(cells["Net Pay"], cells["Gross"] - taken);
    }

    // An absent column is all zeros.
    private static decimal Cell(Dictionary<string, decimal> cells, string header) =>
        cells.GetValueOrDefault(header);

    [Fact]
    public void Tp1ZakatOnly_IsNotShownAsZakatFromPay_AndOtherIsNotNegative()
    {
        var (row, lines, calc) = Calculated((PayrollAdjustmentCategories.DeductZakatTp1, 100m));
        Assert.Equal(100m, calc.Zakat);            // the calculator's PCB offset is untouched
        Assert.Equal(0m, calc.TotalDeductions);

        Assert.Equal(100m, AbPayTimesheetXlsx.SelfPaidPcbOffsets(lines));
        Assert.Equal(0m, AbPayTimesheetXlsx.ZakatFromPay(row.Payslip, lines));
        Assert.Equal(0m, AbPayTimesheetXlsx.OtherDeductions(row.Payslip, lines));

        var (cells, notes) = StatutoryRow(row, lines);
        Assert.DoesNotContain("Zakat", cells.Keys);           // nobody had zakat taken from pay
        Assert.DoesNotContain("Other deductions", cells.Keys);
        Assert.Equal(0m, Cell(cells, "Zakat"));
        Assert.True(Cell(cells, "Other deductions") >= 0m);
        AssertReconciles(cells);
        Assert.Contains(notes, n => n.Contains("Borang TP1 (RM 100.00 in total)"));
    }

    [Fact]
    public void Tp1ZakatPlusPayrollZakat_ShowsOnlyThePayrollZakat()
    {
        var (row, lines, calc) = Calculated(
            (PayrollAdjustmentCategories.DeductZakatTp1, 100m),
            (PayrollAdjustmentCategories.DeductZakat, 60m),
            (PayrollAdjustmentCategories.DeductLoanRepayment, 250m));
        Assert.Equal(160m, calc.Zakat);
        Assert.Equal(310m, calc.TotalDeductions);  // payroll zakat + loan; TP1 is cash-neutral

        var (cells, notes) = StatutoryRow(row, lines);
        Assert.Equal(60m, cells["Zakat"]);
        Assert.Equal(250m, cells["Other deductions"]);   // the loan, nothing netted in twice
        Assert.True(cells["Other deductions"] >= 0m);
        AssertReconciles(cells);
        Assert.Contains(notes, n => n.Contains("Borang TP1 (RM 100.00 in total)"));
    }

    [Fact]
    public void Tp1DepartureLevy_IsNotZakat_AndOtherIsNotNegative()
    {
        var (row, lines, calc) = Calculated((PayrollAdjustmentCategories.DeductDepartureLevyTp1, 200m));
        Assert.Equal(200m, calc.Zakat);            // routed through the same PCB offset
        Assert.Equal(0m, calc.TotalDeductions);

        var (cells, notes) = StatutoryRow(row, lines);
        Assert.DoesNotContain("Zakat", cells.Keys);
        Assert.Equal(0m, Cell(cells, "Zakat"));
        Assert.True(Cell(cells, "Other deductions") >= 0m);
        Assert.Equal(0m, Cell(cells, "Other deductions"));
        AssertReconciles(cells);
        Assert.Contains(notes, n => n.Contains("Borang TP1 (RM 200.00 in total)"));
    }

    // No self-paid offsets, no note about them.
    [Fact]
    public void NoTp1Offsets_NoTp1Note()
    {
        var (row, lines, _) = Calculated((PayrollAdjustmentCategories.DeductZakat, 60m));

        var (cells, notes) = StatutoryRow(row, lines);
        Assert.Equal(60m, cells["Zakat"]);
        AssertReconciles(cells);
        Assert.DoesNotContain(notes, n => n.Contains("Borang TP1"));
    }
}
