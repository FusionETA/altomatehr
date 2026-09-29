using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using static AltomateHR.Api.Modules.LhdnForms.Pdf.LhdnPdfShared;

namespace AltomateHR.Api.Modules.LhdnForms.Pdf;

// PCB 2(II) — Statement of Payment by Employer (PCB 2(II)-Pin. 2012).
//
// Drawn as LHDN's own form, not an AltomateHR summary of it: the same wording,
// the same numbered paragraphs and the same table columns, so it can be signed
// and handed over as it is. No branding, no footer — the official form has
// neither, and an officer compares the two side by side.
//
// One page per employee. Built from a Pcb2IiStatement, so the per-employee
// download (LHDN forms) and the whole-company one (annual reports) draw the
// very same page.
public static class Pcb2IiPdf
{
    public const string ContentType = "application/pdf";

    private const float Label = 190;   // the label column in the particulars block
    private const float Value = 230;   // the underlined value beside it

    public static byte[] Render(LhdnFormPayload payload) => Render([Pcb2IiStatement.From(payload)]);

    public static byte[] Render(IReadOnlyList<Pcb2IiStatement> statements) => Build(statements).GeneratePdf();

    public static IDocument Build(IReadOnlyList<Pcb2IiStatement> statements) =>
        Document.Create(doc =>
        {
            foreach (var s in statements) doc.Page(page => Page(page, s));

            // A company with nobody paid this year is a real state; say so
            // rather than letting QuestPDF refuse an empty document.
            if (statements.Count == 0)
            {
                doc.Page(page =>
                {
                    Setup(page);
                    page.Content().Text("No payroll has been approved for this year, so there is no PCB 2(II) to issue.")
                        .FontSize(9);
                });
            }
        });

    private static void Setup(PageDescriptor page)
    {
        page.Size(PageSizes.A4);
        page.MarginVertical(40);
        page.MarginHorizontal(52);
        page.DefaultTextStyle(t => t.FontFamily(PdfFont.Family).FontSize(9).FontColor(Colors.Black));
    }

    private static void Page(PageDescriptor page, Pcb2IiStatement s)
    {
        Setup(page);
        page.Content().Column(col =>
        {
            // Title line: the form's name left, its code right.
            col.Item().Row(row =>
            {
                row.RelativeItem().Text("STATEMENT OF PAYMENT BY EMPLOYER").Bold();
                row.AutoItem().Text("PCB 2(II)-Pin. 2012");
            });

            col.Item().PaddingTop(12).Row(row =>
            {
                row.RelativeItem().Text("To:");
                row.AutoItem().Text("Tarikh: ");
                row.ConstantItem(70).BorderBottom(0.75f).AlignCenter().Text(FmtDmy(s.Date));
            });
            col.Item().Text("Chief Executive Officer/Director General Inland Revenue");
            col.Item().Text("Inland Revenue Board Of Malaysia");
            col.Item().Row(row =>
            {
                row.ConstantItem(90).Text("Branch");
                row.ConstantItem(150).BorderBottom(0.75f).Text(s.LhdnBranch ?? "");
                row.RelativeItem();
            });

            col.Item().PaddingTop(14).Text("Sir,");

            col.Item().PaddingTop(14).Row(row =>
            {
                row.ConstantItem(Label).Text("Tax Deduction Made During The Year");
                row.ConstantItem(55).BorderBottom(0.75f).AlignCenter().Text(s.Year.ToString());
                row.RelativeItem();
            });
            Particular(col, "Name Of Employee", s.EmployeeName);
            Particular(col, "New Identity Card No./Passport No.", s.IdNumber);
            Particular(col, "Employee Income Tax No.", s.IncomeTaxNumber);
            Particular(col, "Staff No.", s.StaffNo);
            Particular(col, "Employer's No. (E)", s.EmployerNo);

            col.Item().PaddingTop(10).Text("The above matter is hereby referred.");

            col.Item().PaddingTop(12).Text(
                "2. Deductions that have been made to the above employee in the current year are as followed:");
            col.Item().PaddingTop(6).Element(c => CurrentYearTable(c, s));

            col.Item().PaddingTop(14).Text(
                "3. Deductions that have been made to the above employee for the preceeding year income in the current year are as followed:");
            col.Item().PaddingTop(6).PaddingRight(70).Element(PrecedingYearTable);

            col.Item().PaddingTop(14).Text("Thank you.");

            col.Item().PaddingTop(12).Column(sig =>
            {
                Particular(sig, "Name Of Officer", s.OfficerName);
                Particular(sig, "Designation", s.Designation);
                Particular(sig, "Telephone No.", s.Phone);
                Particular(sig, "Name And Address Of Employer", s.EmployerName);
                // The address runs onto the two ruled lines under the name.
                var lines = SplitAddress(s.EmployerAddress);
                Particular(sig, "", lines.Item1);
                Particular(sig, "", lines.Item2);
            });
        });
    }

    // "Label ______value______" — a row of the particulars block.
    private static void Particular(ColumnDescriptor col, string label, string? value) =>
        col.Item().PaddingTop(3).Row(row =>
        {
            row.ConstantItem(Label).Text(label);
            row.ConstantItem(Value + 30).BorderBottom(0.75f).PaddingLeft(2).Text(value ?? "");
            row.RelativeItem();
        });

    private static void CurrentYearTable(IContainer container, Pcb2IiStatement s) =>
        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(1.15f);   // Year / month
                c.RelativeColumn(0.95f);   // Amount: MTD
                c.RelativeColumn(0.95f);   //         CP38
                c.RelativeColumn(1.25f);   // Receipt no.: MTD — LHDN's run long
                c.RelativeColumn(1.25f);   //              CP38
                c.RelativeColumn(0.95f);   // Receipt date: MTD
                c.RelativeColumn(0.95f);   //               CP38
            });

            table.Header(h =>
            {
                Head(h.Cell().RowSpan(2), "Year");
                Head(h.Cell().ColumnSpan(2), "Amount (RM)");
                Head(h.Cell().ColumnSpan(2), "Receipt No./Bank Slip No./\nTransaction No.");
                Head(h.Cell().ColumnSpan(2), "Receipt Date/Transaction Date");
                foreach (var _ in Enumerable.Range(0, 3))
                {
                    Head(h.Cell(), "MTD");
                    Head(h.Cell(), "CP38");
                }
            });

            for (var i = 0; i < 12; i++)
            {
                var m = s.Months[i];
                Cell(table).Text(MonthNames[i]);
                Cell(table).AlignRight().Text(m is null ? "" : FmtRm(m.Mtd));
                Cell(table).AlignRight().Text(m is null ? "" : FmtRm(m.Cp38));
                // A receipt belongs to a payment; print it only where this
                // employee actually had that kind of deduction that month.
                Cell(table).AlignCenter().Text(m is { Mtd: > 0 } ? m.MtdReceiptNo ?? "" : "").FontSize(7);
                Cell(table).AlignCenter().Text(m is { Cp38: > 0 } ? m.Cp38ReceiptNo ?? "" : "").FontSize(7);
                Cell(table).AlignCenter().Text(m is { Mtd: > 0 } ? FmtDmy(m.MtdReceiptDate) : "").FontSize(8);
                Cell(table).AlignCenter().Text(m is { Cp38: > 0 } ? FmtDmy(m.Cp38ReceiptDate) : "").FontSize(8);
            }

            // Only the Year and Amount columns are totalled — the official
            // form draws no cells under the receipt columns on this row.
            Cell(table, 1.25f).Text("Total");
            Cell(table, 1.25f).AlignRight().Text(FmtRm(s.Months.Sum(m => m?.Mtd ?? 0)));
            Cell(table, 1.25f).AlignRight().Text(FmtRm(s.Months.Sum(m => m?.Cp38 ?? 0)));
            table.Cell().ColumnSpan(4);
        });

    // Section 3 — preceding-year income paid this year (arrears, a late bonus).
    // Payroll does not tell such income apart from the current year's, so the
    // rows are left for the officer to fill, as on the official form.
    private static void PrecedingYearTable(IContainer container) =>
        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                for (var i = 0; i < 6; i++) c.RelativeColumn(1);
            });
            table.Header(h =>
            {
                Head(h.Cell(), "Type Of\nIncome");
                Head(h.Cell(), "Month");
                Head(h.Cell(), "Year");
                Head(h.Cell(), "MTD Amount\n(RM)");
                Head(h.Cell(), "Receipt No./\nBank Slip No./\nTransaction No.");
                Head(h.Cell(), "Receipt Date/\nTransaction\nDate");
            });
            for (var i = 0; i < 18; i++) Cell(table).Text("");
        });

    private static void Head(IContainer cell, string text) =>
        cell.Border(0.75f).PaddingVertical(2).PaddingHorizontal(2).AlignCenter().AlignMiddle()
            .Text(text).FontSize(8.5f);

    private static IContainer Cell(TableDescriptor t, float border = 0.75f) =>
        t.Cell().Border(border).MinHeight(12).PaddingHorizontal(3).AlignMiddle();

    private static string FmtDmy(DateTime? d) =>
        d is null ? "" : d.Value.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture);

    // Two ruled lines under the employer's name: break the address at the
    // comma nearest its middle so neither line overflows.
    private static (string?, string?) SplitAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return (null, null);
        var parts = address.Split(", ");
        if (parts.Length < 2 || address.Length <= 48) return (address, null);

        var best = 1;
        for (var i = 1; i < parts.Length; i++)
        {
            var head = string.Join(", ", parts[..i]).Length;
            var prev = string.Join(", ", parts[..best]).Length;
            if (Math.Abs(head - address.Length / 2) < Math.Abs(prev - address.Length / 2)) best = i;
        }
        return (string.Join(", ", parts[..best]) + ",", string.Join(", ", parts[best..]));
    }
}

// Everything one PCB 2(II) page shows. Pure data: the per-employee LHDN form and
// the annual bulk download each build one of these, and the page is drawn from it.
public sealed record Pcb2IiStatement
{
    public required int Year { get; init; }
    public required DateTime Date { get; init; }
    public string? LhdnBranch { get; init; }

    public required string EmployeeName { get; init; }
    public string? IdNumber { get; init; }
    public string? IncomeTaxNumber { get; init; }
    public string? StaffNo { get; init; }
    public string? EmployerNo { get; init; }

    // January..December. Null = no approved payroll that month for this person.
    public required IReadOnlyList<Pcb2IiMonth?> Months { get; init; }

    public string? OfficerName { get; init; }
    public string? Designation { get; init; }
    public string? Phone { get; init; }
    public string? EmployerName { get; init; }
    public string? EmployerAddress { get; init; }

    // The per-employee LHDN form (Employees → LHDN forms).
    public static Pcb2IiStatement From(LhdnFormPayload p) => new()
    {
        Year = p.Year,
        Date = p.GeneratedAt,
        EmployeeName = p.Employee.Name,
        IdNumber = p.Employee.IdNumber,
        IncomeTaxNumber = p.Employee.IncomeTaxNumber,
        StaffNo = p.Employee.EmployeeCode,
        EmployerNo = p.Employer.EmployerTin,
        Months = [.. p.PerMonth.Select(m => m is null ? null : new Pcb2IiMonth(
            m.Mtd, m.Cp38, m.MtdReceiptNo, m.MtdReceiptDate, m.Cp38ReceiptNo, m.Cp38ReceiptDate))],
        OfficerName = p.Employer.DeclarantName,
        Designation = p.Employer.DeclarantPosition,
        Phone = p.Employer.Phone,
        EmployerName = p.Employer.EmployerName ?? p.OrganizationName,
        EmployerAddress = p.Employer.FullAddress,
    };

    // One employee's page in the annual bulk download (Payroll → Annual).
    public static Pcb2IiStatement From(PayrollAnnualPayload payload, AnnualEmployeeRow row, DateTime date)
    {
        var info = payload.CompanyInfo;
        var months = new Pcb2IiMonth?[12];
        foreach (var m in row.Months)
        {
            var r = payload.Receipts.GetValueOrDefault(m.Month);
            months[m.Month - 1] = new Pcb2IiMonth(
                m.Pcb, m.Cp38, r?.PcbReceiptNo, r?.PcbReceiptDate, r?.Cp38ReceiptNo, r?.Cp38ReceiptDate);
        }

        return new Pcb2IiStatement
        {
            Year = payload.Year,
            Date = date,
            EmployeeName = row.EmployeeName,
            IdNumber = row.IdNumber,
            IncomeTaxNumber = row.IncomeTaxNumber,
            StaffNo = row.EmployeeCode,
            EmployerNo = Blank(info?.EmployerTin),
            Months = months,
            OfficerName = Blank(info?.DeclarantName),
            Designation = Blank(info?.DeclarantPosition),
            Phone = Blank(info?.Phone) ?? Blank(info?.Handphone),
            EmployerName = Blank(info?.EmployerName) ?? payload.OrganizationName,
            EmployerAddress = Address(info),
        };
    }

    private static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private static string? Address(Payroll.Entities.PayrollCompanyInfo? info)
    {
        if (info is null) return null;
        var parts = new[]
        {
            info.AddressLine1, info.AddressLine2,
            string.Join(" ", new[] { info.Postcode, info.City }.Where(v => !string.IsNullOrWhiteSpace(v))),
            info.State,
        }.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()).ToList();
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }
}

public sealed record Pcb2IiMonth(
    decimal Mtd, decimal Cp38,
    string? MtdReceiptNo, DateTime? MtdReceiptDate,
    string? Cp38ReceiptNo, DateTime? Cp38ReceiptDate);
