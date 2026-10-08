using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Modules.Payroll.Pdf;
using ClosedXML.Excel;

namespace AltomateHR.Api.Tests.Payroll;

// The Payroll Summary as Excel. What is pinned is what makes it the SAME
// document as the PDF: one row per payslip, the PDF's columns, totals that
// equal both the rows and PayrollSummaryTotals (which the PDF prints), the
// summary block, the itemised breakdown — and that money is stored as real
// numbers, not text.
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

    // The lines the PDF prints under each employee's name.
    [Fact]
    public void BreakdownSheet_ItemisesEachEmployeeAsThePdfDoes()
    {
        var model = ThreeEmployees();
        using var wb = Open(model);
        var sheet = wb.Worksheet(PayrollSummaryXlsx.BreakdownSheet);

        var expected = model.Rows.SelectMany(r => PayrollSummaryPdf
                .Breakdown(r.Payslip, model.LineItems.GetValueOrDefault(r.Payslip.Id) ?? [])
                .Select(l => (r.Payslip.SnapshotEmployeeNumber, l.Label, l.Amount)))
            .ToList();

        var actual = Enumerable.Range(5, expected.Count)
            .Select(r => ((string?)sheet.Cell(r, 1).GetString(), sheet.Cell(r, 3).GetString(),
                sheet.Cell(r, 4).GetValue<decimal>()))
            .ToList();

        Assert.Equal(expected, actual);
        Assert.Contains(actual, a => a.Item2 == "Loan Repayment" && a.Item3 == -100m);
        Assert.True(sheet.Row(5 + expected.Count).IsEmpty());
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
    }
}
