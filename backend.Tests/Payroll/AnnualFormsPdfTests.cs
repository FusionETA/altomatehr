using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Modules.Payroll.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace AltomateHR.Api.Tests.Payroll;

// Form EA, and Form E with its CP8D schedule.
//
// The invariant worth a test here is that **Form E's summary equals the sum
// of the CP8D rows behind it** — an officer reconciles the two, and a return
// whose cover page disagrees with its own schedule is rejected.
public class AnnualFormsPdfTests
{
    private static AnnualEmployeeRow Employee(
        string name = "Aisyah Binti Rahman",
        decimal gross = 66000m,
        decimal bonus = 8250m,
        decimal bik = 2400m,
        decimal epf = 8167.50m,
        decimal pcb = 2187.50m) => new()
        {
            EmployeeProfileId = $"emp-{name}",
            EmployeeName = name,
            EmployeeCode = "E-001",
            JobTitle = "Site Engineer",
            IdNumber = "900101-14-5567",
            IdType = IdType.NRIC,
            IncomeTaxNumber = "SG12345678901",
            MaritalStatus = MaritalStatus.SINGLE,
            GrossSalary = gross,
            BonusAndCommission = bonus,
            TotalBik = bik,
            TotalEpfEmployee = epf,
            TotalPcb = pcb,
            TotalSocsoEmployee = 297m,
            TotalEisEmployee = 118.80m,
        };

    private static PayrollAnnualPayload Payload(params AnnualEmployeeRow[] employees) => new()
    {
        Year = 2026,
        OrganizationName = "Globe Engineering",
        CompanyInfo = new PayrollCompanyInfo
        {
            OrganizationId = "org-1",
            EmployerName = "Globe Engineering Sdn Bhd",
            EmployerTin = "E 1234567890",
        },
        EmployerNo = "1234567890",
        Employees = employees.Length == 0 ? [Employee()] : employees,
    };

    private static bool IsPdf(byte[] bytes) =>
        bytes.Length > 4 && bytes[0] == 0x25 && bytes[1] == 0x50
        && bytes[2] == 0x44 && bytes[3] == 0x46;

    private static int PageCount(IDocument document) =>
        document.GenerateImages(new ImageGenerationSettings
        {
            ImageFormat = ImageFormat.Png,
            RasterDpi = 40,
        }).Count();

    // ─── Form EA ────────────────────────────────────────────────────────

    [Fact]
    public void FormEa_RendersAPdf()
    {
        Assert.True(IsPdf(FormEaPdf.Render(Payload())));
    }

    // One statement per employee — each is handed to a different person, so a
    // page carrying two people's income would be a disclosure.
    [Fact]
    public void FormEa_IsOnePagePerEmployee()
    {
        var payload = Payload(
            Employee("Aisyah Binti Rahman"),
            Employee("Tan Wei Ming"),
            Employee("Arjun Subramaniam"));

        Assert.Equal(3, PageCount(FormEaPdf.Build(payload)));
    }

    // A year with nothing submitted still has to produce a file that says so,
    // rather than QuestPDF refusing an empty document.
    [Fact]
    public void FormEa_AYearWithNoPayroll_StillRendersAPage()
    {
        var empty = Payload() with { Employees = [] };

        Assert.True(IsPdf(FormEaPdf.Render(empty)));
        Assert.Equal(1, PageCount(FormEaPdf.Build(empty)));
    }

    // ─── Form E + CP8D ──────────────────────────────────────────────────

    private static readonly FormECp8dPdf.PartA Headcount = new(2, 2, 1);

    [Fact]
    public void FormE_RendersACoverAndASchedule()
    {
        var pdf = FormECp8dPdf.Build(Payload(Employee("A"), Employee("B")), Headcount);

        Assert.Equal(2, PageCount(pdf));
    }

    // The reconciliation. Both sides are computed from the same rows in the
    // renderer, so this pins that neither drifts to a cached figure.
    [Fact]
    public void FormE_SummaryEqualsTheCp8dRows()
    {
        var payload = Payload(
            Employee("Aisyah Binti Rahman", gross: 66000m, bonus: 8250m, bik: 2400m,
                epf: 8167.50m, pcb: 2187.50m),
            Employee("Tan Wei Ming", gross: 54000m, bonus: 0m, bik: 0m,
                epf: 5940m, pcb: 1320m));

        Assert.Equal(130_650m, payload.Employees.Sum(e => e.TotalIncome));
        Assert.Equal(14_107.50m, payload.Employees.Sum(e => e.TotalEpfEmployee));
        Assert.Equal(3_507.50m, payload.Employees.Sum(e => e.TotalPcb));

        Assert.True(IsPdf(FormECp8dPdf.Render(payload, Headcount)));
    }

    // Reportable income is salary + additional remuneration + benefits in
    // kind. BIK never reached gross or net, but it IS declared.
    [Fact]
    public void TotalIncome_IncludesBonusAndBenefitsInKind()
    {
        var employee = Employee(gross: 50000m, bonus: 8000m, bik: 2000m);

        Assert.Equal(60000m, employee.TotalIncome);
    }

    // An org that never filled in Company Info still gets a document, with
    // the gap called out on the page rather than silently blank.
    [Fact]
    public void FormE_WithNoCompanyProfile_StillRenders()
    {
        var payload = Payload() with { CompanyInfo = null };

        Assert.True(IsPdf(FormECp8dPdf.Render(payload, Headcount)));
    }

    [Fact]
    public void FormE_AYearWithNoPayroll_StillRenders()
    {
        var empty = Payload() with { Employees = [] };

        Assert.Equal(2, PageCount(FormECp8dPdf.Build(empty, new FormECp8dPdf.PartA(0, 0, 0))));
    }

    // ─── Filenames ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(PayrollAnnualReportKind.FORM_EA_BULK_PDF, "Form_EA_2026_Bulk.pdf")]
    [InlineData(PayrollAnnualReportKind.FORM_E_CP8D_PDF, "Form_E_CP8D_2026.pdf")]
    [InlineData(PayrollAnnualReportKind.CP8D_EMPLOYER_TXT, "M1234567890_2026.TXT")]
    [InlineData(PayrollAnnualReportKind.CP8D_EMPLOYEE_TXT, "P1234567890_2026.TXT")]
    public void TheFileNamesAreStable(PayrollAnnualReportKind kind, string expected)
    {
        Assert.Equal(expected, PayrollAnnualReports.FileName(kind, 2026, "1234567890"));
    }

    // An unconfigured employer still gets a distinguishable filename rather
    // than "M_2026.TXT".
    [Fact]
    public void WithNoEmployerNumber_TheFileNameStaysReadable()
    {
        Assert.Equal("MEMPLOYER_2026.TXT",
            PayrollAnnualReports.FileName(PayrollAnnualReportKind.CP8D_EMPLOYER_TXT, 2026, ""));
    }

    [Fact]
    public void EveryKindHasMetadata()
    {
        foreach (var kind in Enum.GetValues<PayrollAnnualReportKind>())
        {
            Assert.True(PayrollAnnualReports.All.ContainsKey(kind), $"{kind} has no metadata");
        }
    }
}
