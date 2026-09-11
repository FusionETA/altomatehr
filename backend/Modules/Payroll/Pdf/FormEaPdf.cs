using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace AltomateHR.Api.Modules.Payroll.Pdf;

// Form EA — one page per employee, the statement of remuneration an employer
// must hand each of them by 28 February so they can file their own return.
//
// The figures are the year's SUBMITTED runs summed. A draft month is not
// remuneration that was paid, and including one would put a number on an
// employee's tax return that never reached their bank.
public static class FormEaPdf
{
    public const string ContentType = "application/pdf";

    public static byte[] Render(PayrollAnnualPayload payload) => Build(payload).GeneratePdf();

    public static IDocument Build(PayrollAnnualPayload payload) =>
        Document.Create(doc =>
        {
            foreach (var employee in payload.Employees)
            {
                doc.Page(page =>
                {
                    PayrollPdfShared.Frame(page);
                    page.Header().Element(c => Header(c, payload));
                    page.Content().Element(c => Body(c, payload, employee));
                    page.Footer().Element(c => PayrollPdfShared.Footer(
                        c, $"Form EA · {payload.Year} · {EmployerName(payload)}"));
                });
            }

            // A year with no submitted runs is a real state — the page has to
            // say so rather than QuestPDF refusing an empty document.
            if (payload.Employees.Count == 0)
            {
                doc.Page(page =>
                {
                    PayrollPdfShared.Frame(page);
                    page.Header().Element(c => Header(c, payload));
                    page.Content().PaddingTop(20).Text(
                            $"No payroll was submitted for {payload.Year}, so there is nothing to "
                            + "report on Form EA.")
                        .FontSize(9).FontColor(PayrollPdfShared.Muted);
                });
            }
        });

    private static void Header(IContainer container, PayrollAnnualPayload payload) =>
        container.PaddingBottom(8).BorderBottom(1.5f).BorderColor(PayrollPdfShared.Accent)
            .PaddingBottom(6).Column(column =>
            {
                column.Item().Text("EA").FontSize(20).Bold();

                // The Malay title is the one on LHDN's own form, and is what
                // an officer or an employee looks for.
                column.Item().Text(
                        "PENYATA SARAAN DARIPADA PENGGAJIAN BAGI TAHUN BERAKHIR "
                        + $"31 DISEMBER {payload.Year}")
                    .FontSize(8.5f).FontColor(PayrollPdfShared.Muted);

                column.Item().PaddingTop(4).Text(EmployerLine(payload)).FontSize(9).SemiBold();
            });

    private static void Body(
        IContainer container, PayrollAnnualPayload payload, AnnualEmployeeRow employee) =>
        container.PaddingTop(10).Column(column =>
        {
            Section(column, "A. Particulars of employee");
            Field(column, "Name", employee.EmployeeName);
            Field(column, "Position", employee.JobTitle);
            Field(column, "IC no. (new)", employee.IdNumber);
            Field(column, "Income tax no.", employee.IncomeTaxNumber);
            Field(column, "EPF no.", employee.EpfNumber);
            Field(column, "SOCSO no.", employee.SocsoNumber);
            Field(column, "Employee no.", employee.EmployeeCode);

            Section(column, "B. Income from employment");
            Amount(column, "Gross salary, wages and overtime", employee.GrossSalary);
            Amount(column, "Bonus, commission, fees and arrears", employee.BonusAndCommission);

            // BIK never touched gross or net, but it IS reportable income —
            // leaving it off understates what the employee must declare.
            Amount(column, "Benefits in kind and perquisites", employee.TotalBik);
            Total(column, "Total income", employee.TotalIncome);

            Section(column, "D. Total deductions");
            Amount(column, "PCB / MTD remitted to LHDN", employee.TotalPcb);
            Amount(column, "CP38 (court-ordered deductions)", employee.TotalCp38);
            Amount(column, "Zakat deducted through payroll", employee.TotalZakat);

            Section(column, "E. Contributions by the employee");
            Amount(column, "EPF (employee share)", employee.TotalEpfEmployee);
            Amount(column, "SOCSO and EIS (employee share)",
                employee.TotalSocsoEmployee + employee.TotalEisEmployee);

            if (employee.QualifyingChildren > 0)
            {
                Section(column, "F. Relief claimed through PCB");
                Field(column, "Qualifying children",
                    employee.QualifyingChildren.ToString());
                Amount(column, "Annual child relief", employee.AnnualChildRelief);
            }

            column.Item().PaddingTop(16).Text(
                    $"Issued by the employer for the year of assessment {payload.Year}.")
                .FontSize(8).FontColor(PayrollPdfShared.Muted);
        });

    private static void Section(ColumnDescriptor column, string text) =>
        column.Item().PaddingTop(12).PaddingBottom(4).Text(text).FontSize(9.5f).Bold();

    private static void Field(ColumnDescriptor column, string label, string? value) =>
        column.Item().PaddingVertical(2).BorderBottom(0.25f).BorderColor(PayrollPdfShared.Rule)
            .PaddingBottom(2).Row(row =>
            {
                row.RelativeItem().Text(label).FontSize(8.5f).FontColor(PayrollPdfShared.Muted);
                row.ConstantItem(240).AlignRight()
                    .Text(string.IsNullOrWhiteSpace(value) ? "—" : value).FontSize(8.5f);
            });

    private static void Amount(ColumnDescriptor column, string label, decimal amount) =>
        column.Item().PaddingVertical(2).BorderBottom(0.25f).BorderColor(PayrollPdfShared.Rule)
            .PaddingBottom(2).Row(row =>
            {
                row.RelativeItem().Text(label).FontSize(8.5f);
                row.ConstantItem(110).AlignRight()
                    .Text(PayrollPdfShared.Rm(amount)).FontSize(8.5f);
            });

    private static void Total(ColumnDescriptor column, string label, decimal amount) =>
        column.Item().PaddingTop(3).BorderTop(1).BorderColor(PayrollPdfShared.Rule)
            .PaddingTop(3).Row(row =>
            {
                row.RelativeItem().Text(label).FontSize(9).Bold();
                row.ConstantItem(110).AlignRight()
                    .Text(PayrollPdfShared.Rm(amount)).FontSize(9).Bold();
            });

    internal static string EmployerName(PayrollAnnualPayload payload) =>
        string.IsNullOrWhiteSpace(payload.CompanyInfo?.EmployerName)
            ? payload.OrganizationName
            : payload.CompanyInfo!.EmployerName!;

    private static string EmployerLine(PayrollAnnualPayload payload) =>
        string.IsNullOrWhiteSpace(payload.CompanyInfo?.EmployerTin)
            ? EmployerName(payload)
            : $"{EmployerName(payload)} · {payload.CompanyInfo!.EmployerTin}";
}
