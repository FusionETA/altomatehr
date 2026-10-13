using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Modules.Payroll.Pdf;
using AltomateHR.Api.Modules.Policies.Entities;
using ClosedXML.Excel;

namespace AltomateHR.Api.Tests.Payroll;

// The Payroll Summary as Excel. What is pinned is what makes it the SAME
// document as the PDF: one row per payslip, the PDF's columns, totals that
// equal both the rows and PayrollSummaryTotals (which the PDF prints), the
// summary block, the itemised lines laid out one column per pay item — and
// that money is stored as real numbers, not text.
public class PayrollSummaryXlsxTests
{
    private static Payslip Payslip(
        string id, string name, string number, decimal gross, decimal pcb,
        decimal epfEe, decimal epfEr, decimal skbbk = 0m, decimal hrdf = 0m, decimal zakat = 0m)
    {
        const decimal socsoEe = 24.75m, eisEe = 9.90m;
        return new Payslip
        {
            Id = id,
            OrganizationId = "org-1",
            PayrollRunId = "run-1",
            SnapshotName = name,
            SnapshotEmployeeNumber = number,
            SnapshotPosition = "Executive",
            ProratedPay = gross,
            GrossPay = gross,
            Pcb = pcb,
            EpfEmployee = epfEe,
            SocsoEmployee = socsoEe,
            EisEmployee = eisEe,
            SkbbkEmployee = skbbk,
            Zakat = zakat,
            NetPay = gross - pcb - epfEe - socsoEe - eisEe - skbbk - zakat,
            EpfEmployer = epfEr,
            SocsoEmployer = 86.65m,
            EisEmployer = 9.90m,
            Hrdf = hrdf,
            HrdfWage = hrdf > 0m ? gross : 0m,
            TotalCostToEmployer = gross + epfEr + 86.65m + 9.90m + hrdf,
        };
    }

    private static PayrollDocumentModel Model(params Payslip[] payslips) => new()
    {
        Run = new PayrollRun
        {
            Id = "run-1", PeriodYear = 2026, PeriodMonth = 7, Status = PayrollRunStatus.SUBMITTED,
            SubmittedAt = new DateTime(2026, 8, 2, 3, 15, 0, DateTimeKind.Utc),
        },
        OrganizationName = "Globe Engineering Sdn Bhd",
        PeriodLabel = "July 2026",
        StatusLabel = "Submitted",
        IssueDate = new DateTime(2026, 7, 31),
        GeneratedAt = new DateTime(2026, 10, 8, 14, 30, 0),
        Rows = payslips.Select(p => new StatutoryEmployeeRow
        {
            Payslip = p, EmployeeName = p.SnapshotName, EmployeeCode = p.SnapshotEmployeeNumber ?? "",
        }).ToList(),
        LineItems = new Dictionary<string, IReadOnlyList<PayslipLineItem>>
        {
            ["ps-1"] =
            [
                new() { PayslipId = "ps-1", Kind = PayslipLineKind.ALLOWANCE, Label = "Travel Allowance",
                        Amount = 250m, Category = PayrollAdjustmentCategories.AllowanceStandard },
                new() { PayslipId = "ps-1", Kind = PayslipLineKind.DEDUCTION, Label = "Loan Repayment",
                        Amount = 100m, Category = PayrollAdjustmentCategories.DeductLoanRepayment },
            ],
        },
    };

    private static PayrollDocumentModel ThreeEmployees() => Model(
        Payslip("ps-1", "Aisyah Binti Rahman", "00027", 5250.00m, 110.35m, 578m, 683m, skbbk: 1.25m, hrdf: 52.50m),
        Payslip("ps-2", "Bala a/l Muthu", "00103", 3333.33m, 0m, 367m, 434m, zakat: 45.10m),
        Payslip("ps-3", "Chong Wei Ming", "E-7", 12875.67m, 1873.45m, 1417m, 1546m, skbbk: 1.25m, hrdf: 128.76m));

    private static XLWorkbook Open(PayrollDocumentModel model) =>
        new(new MemoryStream(PayrollSummaryXlsx.Render(model)));

    // Column of an amount on the Summary sheet, by heading.
    private static int Col(IXLWorksheet sheet, string header) =>
        sheet.Row(PayrollSummaryXlsx.HeaderRow).CellsUsed()
            .Single(c => c.GetString() == header).Address.ColumnNumber;

    private static readonly (string Header, Func<Payslip, decimal> Value, Func<PayrollSummaryTotals, decimal> Total)[] Columns =
    [
        ("Gross", p => p.GrossPay, t => t.Gross),
        ("PCB", p => p.Pcb, t => t.Pcb),
        ("EPF (employee)", p => p.EpfEmployee, t => t.EpfEmp),
        ("SOCSO (employee)", p => p.SocsoEmployee, t => t.SocsoEmp),
        ("EIS (employee)", p => p.EisEmployee, t => t.EisEmp),
        ("SKBBK (employee)", p => p.SkbbkEmployee, t => t.SkbbkEmp),
        ("Net", p => p.NetPay, t => t.Net),
        ("EPF (employer)", p => p.EpfEmployer, t => t.EpfEr),
        ("SOCSO (employer)", p => p.SocsoEmployer, t => t.SocsoEr),
        ("EIS (employer)", p => p.EisEmployer, t => t.EisEr),
        ("HRDF", p => p.Hrdf, t => t.Hrdf),
        ("Cost to employer", p => p.TotalCostToEmployer, t => t.Cost),
    ];

    [Fact]
    public void HasTheThreeSheets_SummaryFirst()
    {
        using var wb = Open(ThreeEmployees());

        Assert.Equal(
            [PayrollSummaryXlsx.SummarySheet, PayrollSummaryXlsx.TotalsSheet, PayrollSummaryXlsx.BreakdownSheet],
            wb.Worksheets.Select(w => w.Name));
    }

    [Fact]
    public void HeaderBlock_NamesTheCompanyPeriodStatusAndDates()
    {
        using var wb = Open(ThreeEmployees());
        var sheet = wb.Worksheet(PayrollSummaryXlsx.SummarySheet);

        Assert.Equal("Globe Engineering Sdn Bhd", sheet.Cell(1, 1).GetString());
        Assert.Contains("July 2026", sheet.Cell(2, 1).GetString());
        Assert.Equal("Submitted", sheet.Cell(3, 2).GetString());

        // Approved 03:15 UTC on 2 Aug = 11:15 in Kuala Lumpur, same day.
        Assert.Equal(XLDataType.DateTime, sheet.Cell(3, 5).DataType);
        Assert.Equal(new DateTime(2026, 8, 2), sheet.Cell(3, 5).GetDateTime());
        Assert.Equal(XLDataType.DateTime, sheet.Cell(4, 2).DataType);
        Assert.Equal(new DateTime(2026, 10, 8, 14, 30, 0), sheet.Cell(4, 2).GetDateTime());
    }

    [Fact]
    public void OneRowPerPayslip_InTheModelsOrder_EmployeeNumberAsText()
    {
        var model = ThreeEmployees();
        using var wb = Open(model);
        var sheet = wb.Worksheet(PayrollSummaryXlsx.SummarySheet);
        var first = PayrollSummaryXlsx.HeaderRow + 1;

        for (var i = 0; i < model.Rows.Count; i++)
        {
            var p = model.Rows[i].Payslip;
            Assert.Equal(XLDataType.Text, sheet.Cell(first + i, 1).DataType);
            Assert.Equal(p.SnapshotEmployeeNumber, sheet.Cell(first + i, 1).GetString());
            Assert.Equal(p.SnapshotName, sheet.Cell(first + i, 2).GetString());

            foreach (var (header, value, _) in Columns)
                Assert.Equal(value(p), sheet.Cell(first + i, Col(sheet, header)).GetValue<decimal>());
        }

        // The row after the last employee is the totals row, then nothing.
        var totalsRow = first + model.Rows.Count;
        Assert.StartsWith("Total (3 employees)", sheet.Cell(totalsRow, 2).GetString());
        Assert.True(sheet.Row(totalsRow + 1).IsEmpty());
    }

    [Fact]
    public void Totals_EqualTheSumOfTheRows_AndThePdfsTotals()
    {
        var model = ThreeEmployees();
        var pdfTotals = PayrollSummaryTotals.Of(model.Rows.Select(r => r.Payslip));
        using var wb = Open(model);
        var sheet = wb.Worksheet(PayrollSummaryXlsx.SummarySheet);
        var first = PayrollSummaryXlsx.HeaderRow + 1;
        var totalsRow = first + model.Rows.Count;

        foreach (var (header, _, total) in Columns)
        {
            var col = Col(sheet, header);
            var cell = sheet.Cell(totalsRow, col);

            // A value, not a formula — it is the PDF's figure.
            Assert.False(cell.HasFormula);
            var sumOfRows = Enumerable.Range(first, model.Rows.Count)
                .Sum(r => sheet.Cell(r, col).GetValue<decimal>());

            Assert.Equal(sumOfRows, cell.GetValue<decimal>());
            Assert.Equal(total(pdfTotals), cell.GetValue<decimal>());
        }
    }

    [Fact]
    public void Money_IsNumeric_WithATwoDecimalFormat()
    {
        var model = ThreeEmployees();
        using var wb = Open(model);
        var sheet = wb.Worksheet(PayrollSummaryXlsx.SummarySheet);
        var first = PayrollSummaryXlsx.HeaderRow + 1;

        for (var r = first; r <= first + model.Rows.Count; r++)
        {
            foreach (var (header, _, _) in Columns)
            {
                var cell = sheet.Cell(r, Col(sheet, header));
                Assert.Equal(XLDataType.Number, cell.DataType);
                Assert.Equal("#,##0.00", cell.Style.NumberFormat.Format);
            }
        }
    }

    [Fact]
    public void Summary_FreezesTheHeader_AndFiltersTheEmployeesOnly()
    {
        var model = ThreeEmployees();
        using var wb = Open(model);
        var sheet = wb.Worksheet(PayrollSummaryXlsx.SummarySheet);

        Assert.Equal(PayrollSummaryXlsx.HeaderRow, sheet.SheetView.SplitRow);
        var filter = sheet.AutoFilter.Range;
        Assert.Equal(PayrollSummaryXlsx.HeaderRow, filter.FirstRow().RowNumber());
        Assert.Equal(PayrollSummaryXlsx.HeaderRow + model.Rows.Count, filter.LastRow().RowNumber());
    }

    // The PDF's closing summary block, line for line.
    [Fact]
    public void TotalsSheet_IsThePdfsSummaryBlock()
    {
        var model = ThreeEmployees();
        var expected = PayrollSummaryTotals.Of(model.Rows.Select(r => r.Payslip)).SummaryLines();
        using var wb = Open(model);
        var sheet = wb.Worksheet(PayrollSummaryXlsx.TotalsSheet);

        for (var i = 0; i < expected.Count; i++)
        {
            var row = 4 + i;
            Assert.Equal(expected[i].Label, sheet.Cell(row, 1).GetString());
            Assert.Equal(XLDataType.Number, sheet.Cell(row, 2).DataType);
            Assert.Equal(expected[i].Value, sheet.Cell(row, 2).GetValue<decimal>());
        }

        Assert.Equal(3m, sheet.Cell(4, 2).GetValue<decimal>()); // Number of employees
        Assert.Contains(expected, l => l.Label == "Total SKBBK payment" && l.Value == 2.50m);
        Assert.Contains(expected, l => l.Label == "Total Zakat payment" && l.Value == 45.10m);
        Assert.True(sheet.Row(4 + expected.Count).IsEmpty());
    }

    // ─── Breakdown: one row per employee, one column per pay item ──────

    private static readonly string[] FixedHeaders =
        ["Basic", "U/L", "Travelling", "Meal", "Parking", "Hours", "OT", "Comm", "Bonus", "Deduction"];

    private const int BHeader = PayrollSummaryXlsx.BreakdownHeaderRow;

    private static List<string> BreakdownHeaders(IXLWorksheet sheet)
    {
        var headers = new List<string>();
        for (var c = 1; sheet.Cell(BHeader, c).GetString() is { Length: > 0 } h; c++) headers.Add(h);
        return headers;
    }

    // Money columns (added into Total Gross): Basic … the last column before
    // Total Gross, without Hours. Not-in-total: everything after Total Gross.
    private static (List<int> Money, int TotalGross, List<int> NotInTotal) BreakdownColumns(IXLWorksheet sheet)
    {
        var headers = BreakdownHeaders(sheet);
        var basic = headers.IndexOf("Basic") + 1;
        var tg = headers.IndexOf("Total Gross") + 1;
        var money = Enumerable.Range(basic, tg - basic).Where(c => headers[c - 1] != "Hours").ToList();
        var notInTotal = Enumerable.Range(tg + 1, headers.Count - tg).ToList();
        return (money, tg, notInTotal);
    }

    // A payslip built by the real PayslipCalculator, so gross, net and the
    // line items follow its semantics.
    private static (Payslip Payslip, List<PayslipLineItem> Lines) Calculated(
        string id, string name, string number, decimal salary, decimal otHours,
        (string Category, string? Name, decimal Amount)[] fixedRows,
        (string Label, decimal Amount)[]? manual = null,
        (string ClaimId, string Label, decimal Amount)[]? claims = null)
    {
        var calc = PayslipCalculator.Calculate(new PayslipCalculator.Input
        {
            PeriodYear = 2026,
            PeriodMonth = 7,
            SalaryType = SalaryType.MONTHLY,
            MonthlySalary = salary,
            OtNormalHours = otHours,
            FixedAllowances = [.. fixedRows.Select(r => new FixedAllowance
            {
                Category = r.Category, Name = r.Name, Amount = r.Amount, TreatAsRecurring = false,
            })],
            ManualDeductions = [.. (manual ?? []).Select(m => new PayslipCalculator.ManualDeduction(m.Label, m.Amount))],
            Reimbursements = [.. (claims ?? []).Select(c => new PayslipCalculator.Reimbursement(c.ClaimId, c.Label, c.Amount))],
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
            Id = id,
            OrganizationId = "org-1",
            PayrollRunId = "run-1",
            SnapshotName = name,
            SnapshotEmployeeNumber = number,
            SnapshotPosition = "Executive",
            BasicPay = calc.BasicPay,
            ProratedPay = calc.ProratedPay,
            OtNormalHours = calc.OtNormalHours,
            OtPay = calc.OtPay,
            TotalAllowances = calc.TotalAllowances,
            TotalReimbursements = calc.TotalReimbursements,
            TotalDeductions = calc.TotalDeductions,
            TotalBenefitsInKind = calc.TotalBenefitsInKind,
            GrossPay = calc.GrossPay,
            EpfEmployee = calc.EpfEmployee, EpfEmployer = calc.EpfEmployer,
            SocsoEmployee = calc.SocsoEmployee, SocsoEmployer = calc.SocsoEmployer,
            EisEmployee = calc.EisEmployee, EisEmployer = calc.EisEmployer,
            SkbbkEmployee = calc.SkbbkEmployee,
            Pcb = calc.Pcb,
            VoluntaryPcb = calc.VoluntaryPcb,
            Cp38 = calc.Cp38,
            Zakat = calc.Zakat,
            Hrdf = calc.Hrdf,
            NetPay = calc.NetPay,
            TotalCostToEmployer = calc.TotalCostToEmployer,
        };
        var lines = calc.LineItems.Select((li, i) => new PayslipLineItem
        {
            PayslipId = id,
            Kind = li.Kind,
            Label = li.Label,
            Amount = li.Amount,
            Category = li.Category,
            ClaimId = li.ClaimId,
            SortOrder = i,
        }).ToList();

        return (payslip, lines);
    }

    // Every kind of item the Breakdown has to place: mapped columns, extra
    // earnings, claims, extra deductions (incl. a free-text one named
    // "Deduction" and a gross-reducing salary adjustment), statutory line
    // items, cash-neutral TP1 items and a benefit in kind.
    private static PayrollDocumentModel Mixed()
    {
        var a = Calculated("ps-a", "Aisyah Binti Rahman", "00027", 4000m, otHours: 10m,
        [
            (PayrollAdjustmentCategories.AllowanceTravelPrivate, null, 200m),
            (PayrollAdjustmentCategories.AllowanceMeal, null, 150m),
            (PayrollAdjustmentCategories.AllowancePhoneFixed, null, 120m),
            (PayrollAdjustmentCategories.WagesCommission, null, 300m),
            (PayrollAdjustmentCategories.BikCar, null, 500m),
            (PayrollAdjustmentCategories.DeductUnpaidLeave, null, 100m),
            (PayrollAdjustmentCategories.DeductMiscellaneous, null, 50m),
            (PayrollAdjustmentCategories.DeductLoanRepayment, null, 200m),
            (PayrollAdjustmentCategories.DeductCp38, null, 80m),
            (PayrollAdjustmentCategories.DeductAdditionalPcb, null, 30m),
            (PayrollAdjustmentCategories.DeductZakat, null, 40m),
            (PayrollAdjustmentCategories.DeductTp1LifeInsurance, null, 150m),
            (PayrollAdjustmentCategories.DeductZakatTp1, null, 20m),
        ],
        manual: [("Deduction", 25m), ("Uniform", 60m)],
        claims: [("cl-1", "Client lunch", 88.50m), ("cl-2", "Taxi to site", 41.20m)]);

        var b = Calculated("ps-b", "Bala a/l Muthu", "00103", 3000m, otHours: 0m,
        [
            (PayrollAdjustmentCategories.AllowanceStandard, "Shift allowance", 100m),
            (PayrollAdjustmentCategories.DeductSalaryAdjustment, null, 75m),
            (PayrollAdjustmentCategories.DeductLoanRepayment, null, 100m),
        ]);

        var c = Calculated("ps-c", "Chong Wei Ming", "E-7", 12875.67m, otHours: 0m, []);

        return new PayrollDocumentModel
        {
            Run = new PayrollRun { Id = "run-1", PeriodYear = 2026, PeriodMonth = 7, Status = PayrollRunStatus.SUBMITTED },
            OrganizationName = "Globe Engineering Sdn Bhd",
            PeriodLabel = "July 2026",
            StatusLabel = "Submitted",
            IssueDate = new DateTime(2026, 7, 31),
            GeneratedAt = new DateTime(2026, 10, 8, 14, 30, 0),
            Rows =
            [
                new() { Payslip = a.Payslip, EmployeeName = "Aisyah", EmployeeCode = "00027",
                        Department = " Operations ", Location = "KL HQ" },
                new() { Payslip = b.Payslip, EmployeeName = "Bala", EmployeeCode = "00103",
                        Department = "Sales", Location = null },
                new() { Payslip = c.Payslip, EmployeeName = "Chong", EmployeeCode = "E-7" },
            ],
            LineItems = new Dictionary<string, IReadOnlyList<PayslipLineItem>>
            {
                ["ps-a"] = a.Lines, ["ps-b"] = b.Lines, ["ps-c"] = c.Lines,
            },
        };
    }

    [Fact]
    public void Breakdown_FixedColumnsThenOnlyTheExtrasPresent_ThenTotalGross()
    {
        using var wb = Open(ThreeEmployees());   // ps-1: a standard allowance + a loan
        var sheet = wb.Worksheet(PayrollSummaryXlsx.BreakdownSheet);

        string[] expected =
        [
            "Emp no.", "Staff", "Group", "Outlet", .. FixedHeaders,
            "Standard Allowance", "Loan Repayment", "Total Gross",
        ];
        Assert.Equal(expected, BreakdownHeaders(sheet));
        // No not-in-total band when nothing needs it.
        Assert.True(sheet.Row(BHeader - 1).IsEmpty());
    }

    [Fact]
    public void Breakdown_NoLineItems_IsTheFixedColumnsAndTotalGross()
    {
        var model = ThreeEmployees() with { LineItems = new Dictionary<string, IReadOnlyList<PayslipLineItem>>() };
        using var wb = Open(model);

        Assert.Equal(
            ["Emp no.", "Staff", "Group", "Outlet", .. FixedHeaders, "Total Gross"],
            BreakdownHeaders(wb.Worksheet(PayrollSummaryXlsx.BreakdownSheet)));
    }

    [Fact]
    public void Breakdown_ExtraEarningsThenDeductions_ThenTotalGross_ThenNotInTotal()
    {
        using var wb = Open(Mixed());
        var sheet = wb.Worksheet(PayrollSummaryXlsx.BreakdownSheet);

        string[] expected =
        [
            "Emp no.", "Staff", "Group", "Outlet", .. FixedHeaders,
            // earnings, first appearance
            "Phone Allowance (Fixed)", "Claims", "Standard Allowance",
            // deductions taken from pay, first appearance; the free-text one
            // called "Deduction" must not pose as the fixed column
            "Loan Repayment", "CP38 Arrears (LHDN Order)", "Deduction (2)", "Uniform", "Salary Adjustment",
            "Total Gross",
            // not in Total Gross: BIK, statutory line items, cash-neutral TP1
            "Car/Petrol BIK (non-cash)", "Additional PCB", "Zakat — via salary deduction (PZB)",
            "TP1 · Life Insurance", "Zakat — self-paid (TP1)",
        ];
        Assert.Equal(expected, BreakdownHeaders(sheet));

        // The band names the not-in-total columns.
        var (_, tg, notInTotal) = BreakdownColumns(sheet);
        var band = sheet.Cell(BHeader - 1, notInTotal[0]);
        Assert.Equal("NOT IN TOTAL GROSS", band.GetString());
        Assert.True(band.IsMerged());
        Assert.Equal("", sheet.Cell(BHeader - 1, tg).GetString());
    }

    [Fact]
    public void Breakdown_RowsAreTheMappedValues_WithIdentityAndSigns()
    {
        var model = Mixed();
        using var wb = Open(model);
        var sheet = wb.Worksheet(PayrollSummaryXlsx.BreakdownSheet);
        var headers = BreakdownHeaders(sheet);
        decimal At(int row, string header) => sheet.Cell(row, headers.IndexOf(header) + 1).GetValue<decimal>();

        var a = BHeader + 1;
        Assert.Equal(XLDataType.Text, sheet.Cell(a, 1).DataType);
        Assert.Equal("00027", sheet.Cell(a, 1).GetString());
        Assert.Equal("Aisyah Binti Rahman", sheet.Cell(a, 2).GetString());   // snapshot name
        Assert.Equal("Operations", sheet.Cell(a, 3).GetString());            // Department, trimmed
        Assert.Equal("KL HQ", sheet.Cell(a, 4).GetString());                 // Location
        Assert.Equal("", sheet.Cell(a + 1, 4).GetString());

        var pa = model.Rows[0].Payslip;
        Assert.Equal(pa.ProratedPay, At(a, "Basic"));
        Assert.Equal(-100m, At(a, "U/L"));
        Assert.Equal(200m, At(a, "Travelling"));
        Assert.Equal(150m, At(a, "Meal"));
        Assert.Equal(10m, At(a, "Hours"));
        Assert.True(pa.OtPay > 0m);
        Assert.Equal(pa.OtPay, At(a, "OT"));
        Assert.Equal(300m, At(a, "Comm"));
        Assert.Equal(-50m, At(a, "Deduction"));
        Assert.Equal(120m, At(a, "Phone Allowance (Fixed)"));
        Assert.Equal(129.70m, At(a, "Claims"));                    // both claims, one column
        Assert.Equal(-200m, At(a, "Loan Repayment"));
        Assert.Equal(-80m, At(a, "CP38 Arrears (LHDN Order)"));
        Assert.Equal(-25m, At(a, "Deduction (2)"));
        Assert.Equal(-60m, At(a, "Uniform"));
        Assert.Equal(500m, At(a, "Car/Petrol BIK (non-cash)"));
        Assert.Equal(-30m, At(a, "Additional PCB"));
        Assert.Equal(-40m, At(a, "Zakat — via salary deduction (PZB)"));
        Assert.Equal(-150m, At(a, "TP1 · Life Insurance"));
        Assert.Equal(-20m, At(a, "Zakat — self-paid (TP1)"));

        // Bala: the standard allowance (named "Shift allowance" by the admin)
        // under its category; the gross-reducing salary adjustment negative.
        Assert.Equal(100m, At(a + 1, "Standard Allowance"));
        Assert.Equal(-75m, At(a + 1, "Salary Adjustment"));
        Assert.Equal(0m, At(a + 1, "Phone Allowance (Fixed)"));
    }

    [Fact]
    public void Breakdown_EveryRowsTotalGross_IsItsMoneyColumnsSummed()
    {
        var model = Mixed();
        using var wb = Open(model);
        var sheet = wb.Worksheet(PayrollSummaryXlsx.BreakdownSheet);
        var (money, tg, _) = BreakdownColumns(sheet);

        for (var i = 0; i < model.Rows.Count; i++)
        {
            var r = BHeader + 1 + i;
            var sum = money.Sum(c => sheet.Cell(r, c).GetValue<decimal>());
            Assert.Equal(sum, sheet.Cell(r, tg).GetValue<decimal>());
            Assert.False(sheet.Cell(r, tg).HasFormula);

            // For a generated payslip: gross less every non-statutory deduction
            // taken from net pay (loans, miscellaneous, CP38, free-text).
            var p = model.Rows[i].Payslip;
            var items = model.LineItems[p.Id];
            Assert.Equal(p.GrossPay - AbPayTimesheetXlsx.OtherDeductions(p, items),
                sheet.Cell(r, tg).GetValue<decimal>());
        }
    }

    // Nothing dropped, nothing counted twice: every line the old long-list
    // Breakdown (PayrollSummaryPdf.Breakdown) printed lands in exactly one
    // column, so each row's money + not-in-total columns add up to that
    // employee's lines, and the sheet's to the run's.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Breakdown_ReconcilesWithTheItemisedLines(bool mixed)
    {
        var model = mixed ? Mixed() : ThreeEmployees();
        using var wb = Open(model);
        var sheet = wb.Worksheet(PayrollSummaryXlsx.BreakdownSheet);
        var (money, _, notInTotal) = BreakdownColumns(sheet);
        var all = money.Concat(notInTotal).ToList();

        var sheetTotal = 0m;
        var linesTotal = 0m;
        for (var i = 0; i < model.Rows.Count; i++)
        {
            var p = model.Rows[i].Payslip;
            var lines = PayrollSummaryPdf.Breakdown(p, model.LineItems.GetValueOrDefault(p.Id) ?? []);
            var r = BHeader + 1 + i;

            var rowSum = all.Sum(c => sheet.Cell(r, c).GetValue<decimal>());
            Assert.Equal(lines.Sum(l => l.Amount), rowSum);

            sheetTotal += rowSum;
            linesTotal += lines.Sum(l => l.Amount);
        }
        Assert.Equal(linesTotal, sheetTotal);

        // And column by column: the absolute amounts too, so a line counted
        // with the wrong sign (or twice and cancelled) cannot hide.
        var absSheet = Enumerable.Range(BHeader + 1, model.Rows.Count)
            .Sum(r => all.Sum(c => Math.Abs(sheet.Cell(r, c).GetValue<decimal>())));
        var absLines = model.Rows.Sum(row => PayrollSummaryPdf
            .Breakdown(row.Payslip, model.LineItems.GetValueOrDefault(row.Payslip.Id) ?? [])
            .Sum(l => Math.Abs(l.Amount)));
        // (No column on these runs holds lines of opposite signs, so the
        // absolute sums must agree exactly.)
        Assert.Equal(absLines, absSheet);
    }

    [Fact]
    public void Breakdown_TotalsRow_IsEachColumnSummed_Bold_AndOutsideTheFilter()
    {
        var model = Mixed();
        using var wb = Open(model);
        var sheet = wb.Worksheet(PayrollSummaryXlsx.BreakdownSheet);
        var headers = BreakdownHeaders(sheet);
        var first = BHeader + 1;
        var totalsRow = first + model.Rows.Count;

        Assert.Equal("Total (3 employees)", sheet.Cell(totalsRow, 2).GetString());
        for (var c = 5; c <= headers.Count; c++)
        {
            var cell = sheet.Cell(totalsRow, c);
            Assert.False(cell.HasFormula);
            Assert.True(cell.Style.Font.Bold);
            var sum = Enumerable.Range(first, model.Rows.Count).Sum(r => sheet.Cell(r, c).GetValue<decimal>());
            Assert.Equal(sum, cell.GetValue<decimal>());
        }
        Assert.True(sheet.Row(totalsRow + 1).IsEmpty());

        // Frozen header and first two columns; the filter stops above the totals.
        Assert.Equal(BHeader, sheet.SheetView.SplitRow);
        Assert.Equal(2, sheet.SheetView.SplitColumn);
        var filter = sheet.AutoFilter.Range;
        Assert.Equal(BHeader, filter.FirstRow().RowNumber());
        Assert.Equal(totalsRow - 1, filter.LastRow().RowNumber());
        Assert.Equal(headers.Count, filter.LastColumn().ColumnNumber());
    }

    [Fact]
    public void Breakdown_AmountsAreNumeric_TwoDecimals_EmpNoText()
    {
        var model = Mixed();
        using var wb = Open(model);
        var sheet = wb.Worksheet(PayrollSummaryXlsx.BreakdownSheet);
        var headers = BreakdownHeaders(sheet);

        for (var r = BHeader + 1; r <= BHeader + 1 + model.Rows.Count; r++)
        {
            for (var c = 5; c <= headers.Count; c++)
            {
                Assert.Equal(XLDataType.Number, sheet.Cell(r, c).DataType);
                Assert.Equal("#,##0.00", sheet.Cell(r, c).Style.NumberFormat.Format);
            }
        }
        for (var r = BHeader + 1; r < BHeader + 1 + model.Rows.Count; r++)
            Assert.Equal(XLDataType.Text, sheet.Cell(r, 1).DataType);
    }

    [Fact]
    public void Breakdown_TotalGrossHeader_CarriesTheNote()
    {
        using var wb = Open(Mixed());
        var sheet = wb.Worksheet(PayrollSummaryXlsx.BreakdownSheet);
        var (_, tg, _) = BreakdownColumns(sheet);
        var cell = sheet.Cell(BHeader, tg);

        Assert.Equal("Total Gross", cell.GetString());
        Assert.True(cell.HasComment);
        Assert.Equal(
            "Total Gross = all the money columns on this row added up (deductions are shown as negative). "
            + "It differs from the payroll Gross on the Summary sheet, which doesn't subtract deductions "
            + "taken from net pay.",
            cell.GetComment().Text);
    }

    // The summary is every company's document: nothing in it may name ABPay.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Workbook_NeverMentionsAbPay(bool mixed)
    {
        using var wb = Open(mixed ? Mixed() : ThreeEmployees());

        static bool Bad(string text) =>
            text.Contains("abpay", StringComparison.OrdinalIgnoreCase)
            || text.Contains("ab pay", StringComparison.OrdinalIgnoreCase);

        foreach (var sheet in wb.Worksheets)
        {
            Assert.False(Bad(sheet.Name));
            foreach (var cell in sheet.CellsUsed(XLCellsUsedOptions.All))
            {
                Assert.False(Bad(cell.GetString()), $"{sheet.Name}!{cell.Address}: {cell.GetString()}");
                if (cell.HasComment) Assert.False(Bad(cell.GetComment().Text));
            }
        }
    }

    // Lay the workbook down where a human can open it: set
    // PAYROLL_SUMMARY_XLSX_OUT to a directory to get a sample file.
    [Fact]
    public void WritesASample_WhenAsked()
    {
        var dir = Environment.GetEnvironmentVariable("PAYROLL_SUMMARY_XLSX_OUT");
        if (string.IsNullOrEmpty(dir)) return;

        File.WriteAllBytes(Path.Combine(dir, "Payroll_Summary_July_2026.xlsx"),
            PayrollSummaryXlsx.Render(ThreeEmployees()));
        File.WriteAllBytes(Path.Combine(dir, "Payroll_Summary_July_2026_mixed.xlsx"),
            PayrollSummaryXlsx.Render(Mixed()));
    }
}
