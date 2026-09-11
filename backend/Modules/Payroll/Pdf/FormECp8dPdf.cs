using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AltomateHR.Api.Modules.Payroll.Pdf;

// Form E — the employer's own annual return — with the CP8D schedule of
// per-employee particulars behind it.
//
// The two belong in one document because they are filed together and their
// totals have to agree: Form E's summary is the sum of the CP8D rows, and an
// officer reconciles one against the other. Computing the summary from the
// rows shown, rather than from a stored total, is what keeps that true.
public static class FormECp8dPdf
{
    public const string ContentType = "application/pdf";

    // The Part A headcounts. Passed in rather than derived because they are
    // about EMPLOYMENT at points in the year, not about payslips — someone
    // who joined in December and was not paid until January still counts.
    public sealed record PartA(
        int HeadcountAtYearEnd, int HeadcountSubjectToMtd, int NewEmployees);

    public static byte[] Render(PayrollAnnualPayload payload, PartA partA) =>
        Build(payload, partA).GeneratePdf();

    public static IDocument Build(PayrollAnnualPayload payload, PartA partA) =>
        Document.Create(doc =>
        {
            doc.Page(page =>
            {
                PayrollPdfShared.Frame(page);
                page.Header().Element(c => Title(c, $"FORM E {payload.Year}",
                    "RETURN FORM OF EMPLOYER"));
                page.Content().Element(c => FormE(c, payload, partA));
                page.Footer().Element(c => PayrollPdfShared.Footer(
                    c, $"Form E · {payload.Year} · {FormEaPdf.EmployerName(payload)}"));
            });

            doc.Page(page =>
            {
                // Landscape: the schedule is seven columns wide and squeezing
                // a name and two identifiers into a portrait page truncates
                // exactly the fields LHDN matches on.
                page.Size(PageSizes.A4.Landscape());
                page.MarginVertical(30);
                page.MarginHorizontal(30);
                page.DefaultTextStyle(t => t
                    .FontFamily("Helvetica").FontSize(8).FontColor(PayrollPdfShared.Ink));

                page.Header().Element(c => Title(c, "CP8D",
                    $"EMPLOYEES' PARTICULARS · YEAR {payload.Year}"));
                page.Content().Element(c => Cp8dTable(c, payload));
                page.Footer().Element(c => PayrollPdfShared.Footer(
                    c, $"CP8D · {payload.Year} · {FormEaPdf.EmployerName(payload)}"));
            });
        });

    private static void Title(IContainer container, string title, string subtitle) =>
        container.PaddingBottom(8).BorderBottom(1.5f).BorderColor(PayrollPdfShared.Accent)
            .PaddingBottom(6).Column(column =>
            {
                column.Item().Text(title).FontSize(16).Bold();
                column.Item().Text(subtitle).FontSize(8.5f).FontColor(PayrollPdfShared.Muted);
            });

    private static void FormE(IContainer container, PayrollAnnualPayload payload, PartA partA) =>
        container.PaddingTop(10).Column(column =>
        {
            var info = payload.CompanyInfo;

            Section(column, "Basic particulars");
            Field(column, "Employer name", FormEaPdf.EmployerName(payload));
            Field(column, "Employer no. (LHDN E no.)", info?.EmployerTin);
            Field(column, "Registration no.", info?.RegistrationNo);
            Field(column, "Email", info?.Email);
            Field(column, "Phone", info?.Phone);
            Field(column, "Address", Address(payload));

            Section(column, "Part A — headcount");
            Field(column, $"A1. Employees as at 31/12/{payload.Year}",
                partA.HeadcountAtYearEnd.ToString());
            Field(column, "A2. Employees subject to MTD",
                partA.HeadcountSubjectToMtd.ToString());
            Field(column, "A3. New employees during the year",
                partA.NewEmployees.ToString());

            // Summed from the rows on the next page, never from a cached
            // figure — the two have to reconcile or the return is rejected.
            Section(column, "Summary totals");
            Total(column, "Total gross remuneration (incl. bonus and BIK)",
                payload.Employees.Sum(e => e.TotalIncome));
            Total(column, "Total EPF (employee share)",
                payload.Employees.Sum(e => e.TotalEpfEmployee));
            Total(column, "Total PCB / MTD", payload.Employees.Sum(e => e.TotalPcb));

            var count = payload.Employees.Count;
            column.Item().PaddingTop(14).Text(
                    count == 0
                        ? $"No payroll was submitted for {payload.Year}, so the CP8D schedule is empty."
                        : $"The CP8D schedule of {count} employee{(count == 1 ? "" : "s")} follows.")
                .FontSize(8).FontColor(PayrollPdfShared.Muted);

            if (info is null)
            {
                column.Item().PaddingTop(8).Text(
                        "⚠ No company profile is on file, so the employer particulars above are "
                        + "incomplete. Fill in Payroll Settings → Company Info before filing.")
                    .FontSize(8).FontColor(PayrollPdfShared.Muted);
            }
        });

    private static void Cp8dTable(IContainer container, PayrollAnnualPayload payload) =>
        container.PaddingTop(8).Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.ConstantColumn(28);    // no
                c.RelativeColumn(3);     // name
                c.ConstantColumn(90);    // income tax no
                c.ConstantColumn(90);    // ic
                c.ConstantColumn(40);    // category
                c.ConstantColumn(80);    // gross
                c.ConstantColumn(80);    // epf
                c.ConstantColumn(80);    // pcb
            });

            table.Header(header =>
            {
                HeaderCell(header, "No");
                HeaderCell(header, "Name");
                HeaderCell(header, "Income tax no.");
                HeaderCell(header, "IC no.");
                HeaderCell(header, "Cat");
                HeaderCell(header, "Gross", right: true);
                HeaderCell(header, "EPF", right: true);
                HeaderCell(header, "MTD", right: true);
            });

            var index = 1;
            foreach (var e in payload.Employees)
            {
                Cell(table, index++.ToString());
                Cell(table, e.EmployeeName);
                Cell(table, Dash(e.IncomeTaxNumber));
                Cell(table, Dash(e.IdNumber));
                Cell(table, PayrollAnnualReports.TaxCategory(
                    e.MaritalStatus, e.SpouseWorking, e.QualifyingChildren));
                Cell(table, PayrollPdfShared.Rm(e.TotalIncome), right: true);
                Cell(table, PayrollPdfShared.Rm(e.TotalEpfEmployee), right: true);
                Cell(table, PayrollPdfShared.Rm(e.TotalPcb), right: true);
            }

            if (payload.Employees.Count == 0) return;

            // The totals row is what an officer checks against Form E's
            // summary on the previous page.
            TotalCell(table, string.Empty);
            TotalCell(table, "Total");
            TotalCell(table, string.Empty);
            TotalCell(table, string.Empty);
            TotalCell(table, string.Empty);
            TotalCell(table, PayrollPdfShared.Rm(payload.Employees.Sum(e => e.TotalIncome)), true);
            TotalCell(table, PayrollPdfShared.Rm(payload.Employees.Sum(e => e.TotalEpfEmployee)), true);
            TotalCell(table, PayrollPdfShared.Rm(payload.Employees.Sum(e => e.TotalPcb)), true);
        });

    private static void HeaderCell(TableCellDescriptor header, string text, bool right = false)
    {
        var cell = header.Cell().BorderBottom(1).BorderColor(PayrollPdfShared.Accent)
            .PaddingVertical(3).PaddingHorizontal(2);

        (right ? cell.AlignRight() : cell).Text(text).FontSize(7.5f).SemiBold();
    }

    private static void Cell(TableDescriptor table, string text, bool right = false)
    {
        var cell = table.Cell().BorderBottom(0.25f).BorderColor(PayrollPdfShared.Rule)
            .PaddingVertical(2.5f).PaddingHorizontal(2);

        (right ? cell.AlignRight() : cell).Text(text).FontSize(7.5f);
    }

    private static void TotalCell(TableDescriptor table, string text, bool right = false)
    {
        var cell = table.Cell().BorderTop(1).BorderColor(PayrollPdfShared.Accent)
            .PaddingVertical(3).PaddingHorizontal(2);

        (right ? cell.AlignRight() : cell).Text(text).FontSize(8).Bold();
    }

    private static string Dash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value;

    private static string Address(PayrollAnnualPayload payload)
    {
        var info = payload.CompanyInfo;
        if (info is null) return string.Empty;

        return string.Join(", ", new[]
        {
            info.AddressLine1, info.AddressLine2, info.City,
            info.Postcode, info.State, info.Country,
        }.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    private static void Section(ColumnDescriptor column, string text) =>
        column.Item().PaddingTop(12).PaddingBottom(4).Text(text).FontSize(9.5f).Bold();

    private static void Field(ColumnDescriptor column, string label, string? value) =>
        column.Item().PaddingVertical(2).BorderBottom(0.25f).BorderColor(PayrollPdfShared.Rule)
            .PaddingBottom(2).Row(row =>
            {
                row.RelativeItem().Text(label).FontSize(8.5f).FontColor(PayrollPdfShared.Muted);
                row.ConstantItem(280).AlignRight()
                    .Text(string.IsNullOrWhiteSpace(value) ? "—" : value).FontSize(8.5f);
            });

    private static void Total(ColumnDescriptor column, string label, decimal amount) =>
        column.Item().PaddingVertical(2).BorderBottom(0.25f).BorderColor(PayrollPdfShared.Rule)
            .PaddingBottom(2).Row(row =>
            {
                row.RelativeItem().Text(label).FontSize(8.5f).SemiBold();
                row.ConstantItem(110).AlignRight()
                    .Text(PayrollPdfShared.Rm(amount)).FontSize(8.5f).Bold();
            });
}
