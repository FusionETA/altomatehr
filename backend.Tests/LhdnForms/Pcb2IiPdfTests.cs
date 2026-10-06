using AltomateHR.Api.Modules.LhdnForms.Pdf;
using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace AltomateHR.Api.Tests.LhdnForms;

// PCB 2(II) — Statement of Payment by Employer, in LHDN's own layout.
//
// The rules worth pinning: one page per employee (each is handed to a
// different person), and a month's LHDN receipt appears only beside a
// deduction that was actually made — a receipt printed against RM 0.00 reads
// as a payment that never happened.
public class Pcb2IiPdfTests
{
    private static AnnualEmployeeRow Row(string name, params AnnualMonth[] months) => new()
    {
        EmployeeProfileId = $"emp-{name}",
        EmployeeName = name,
        EmployeeCode = "AA0001",
        IdNumber = "900101-14-5567",
        IncomeTaxNumber = "SG12345678901",
        Months = months,
    };

    private static PayrollAnnualPayload Payload(params AnnualEmployeeRow[] rows) => new()
    {
        Year = 2026,
        OrganizationName = "Acme Engineering",
        CompanyInfo = new PayrollCompanyInfo
        {
            OrganizationId = "org-1",
            EmployerName = "Acme Engineering Sdn Bhd",
            EmployerTin = "E 1234567890",
            AddressLine1 = "12 Jalan Teknologi 3/1",
            AddressLine2 = "Taman Sains Selangor",
            Postcode = "47810",
            City = "Petaling Jaya",
            State = "Selangor",
            Phone = "03-6150 1234",
            DeclarantName = "Tan Mei Ling",
            DeclarantPosition = "HR Manager",
        },
        EmployerNo = "1234567890",
        Employees = rows,
        Receipts = new Dictionary<int, LhdnMonthReceipts>
        {
            [1] = new("CP39-202601-88812", new DateTime(2026, 2, 12), "CP38-202601-5521", new DateTime(2026, 2, 12)),
            [2] = new("CP39-202602-90413", new DateTime(2026, 3, 13), null, null),
        },
    };

    private static int PageCount(IDocument document) =>
        document.GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = 40 }).Count();

    private static IReadOnlyList<Pcb2IiStatement> Statements(PayrollAnnualPayload p) =>
        [.. p.Employees.Select(e => Pcb2IiStatement.From(p, e, new DateTime(2026, 9, 29)))];

    [Fact]
    public void IsOnePagePerEmployee()
    {
        var payload = Payload(
            Row("Aisyah Binti Rahman", new AnnualMonth(1, 250m, 0m, 0m)),
            Row("Tan Wei Ming", new AnnualMonth(1, 400m, 0m, 0m)),
            Row("Arjun Subramaniam", new AnnualMonth(2, 90m, 0m, 0m)));

        Assert.Equal(3, PageCount(Pcb2IiPdf.Build(Statements(payload))));
    }

    [Fact]
    public void NobodyPaid_StillProducesAPage()
    {
        Assert.Equal(1, PageCount(Pcb2IiPdf.Build([])));
    }

    [Fact]
    public void AMonthCarriesThatMonthsReceipts()
    {
        var s = Statements(Payload(Row("Aisyah", new AnnualMonth(1, 250m, 420m, 0m), new AnnualMonth(2, 250m, 0m, 0m))))[0];

        Assert.Equal("CP39-202601-88812", s.Months[0]!.MtdReceiptNo);
        Assert.Equal("CP38-202601-5521", s.Months[0]!.Cp38ReceiptNo);
        Assert.Equal("CP39-202602-90413", s.Months[1]!.MtdReceiptNo);
        Assert.Null(s.Months[1]!.Cp38ReceiptNo);
        // No approved payroll in March for this person: the row stays blank.
        Assert.Null(s.Months[2]);
    }

    [Fact]
    public void EmployerBlockComesFromCompanyInfo()
    {
        var s = Statements(Payload(Row("Aisyah", new AnnualMonth(1, 250m, 0m, 0m))))[0];

        Assert.Equal("E 1234567890", s.EmployerNo);
        Assert.Equal("Tan Mei Ling", s.OfficerName);
        Assert.Equal("HR Manager", s.Designation);
        Assert.Equal("12 Jalan Teknologi 3/1, Taman Sains Selangor, 47810 Petaling Jaya, Selangor", s.EmployerAddress);
    }
}
