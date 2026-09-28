using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AltomateHR.Api.Modules.Payroll.Pdf;

// Borang PCB/TP1 (1/2026) for one employee and one payroll month — the form in
// LHDN's layout (MTD Spec 2026 Exhibit 1): A employer, B individual, C
// deductions and D rebates with each item's annual limit, this month's amount
// (SEMASA) and the year to date (TERKUMPUL), then E the employee's declaration
// and F the employer's approval.
//
// The amounts are what payroll GRANTED after LHDN's limits, so the form agrees
// with the PCB actually withheld. The declaration's date and signature are left
// for the employee — the claim was keyed in by the employer, not submitted
// online, and the form must not assert a signature nobody gave.
public static class Tp1FormPdf
{
    public const string ContentType = "application/pdf";

    public sealed record Model
    {
        public required string EmployerName { get; init; }
        public string? EmployerTin { get; init; }
        public string? EmployerAddress { get; init; }

        public required int Year { get; init; }
        public required int Month { get; init; }

        public required string EmployeeName { get; init; }
        public string? EmployeeCode { get; init; }
        public string? IdNumber { get; init; }
        public string? IncomeTaxNumber { get; init; }

        public required IReadOnlyList<Tp1Form.Row> Deductions { get; init; }
        public required IReadOnlyList<Tp1Form.Row> Rebates { get; init; }

        // Bahagian F: when the employer processed the claim into payroll, and
        // who did — the person in charge of that month's payroll.
        public DateTime? ProcessedOn { get; init; }
        public string? ProcessedByName { get; init; }
        public string? ProcessedByDesignation { get; init; }

        public required DateTime GeneratedAt { get; init; }
    }

    public static byte[] Render(Model model) => Build(model).GeneratePdf();

    // The document itself, for rasterising in a visual check.
    public static IDocument Build(Model model) =>
        Document.Create(doc => doc.Page(page =>
        {
            PayrollPdfShared.Frame(page);
            page.MarginHorizontal(34);
            page.Header().Element(c => Header(c, model));
            page.Content().Element(c => Body(c, model));
            page.Footer().Element(c => PayrollPdfShared.Footer(c,
                $"Borang PCB/TP1 (1/2026) · {model.EmployeeName} · {MonthName(model.Month)} {model.Year} · "
                + $"dijana {PayrollPdfShared.Date(model.GeneratedAt)}"));
        }));

    private static void Header(IContainer container, Model m) =>
        container.PaddingBottom(6).Column(col =>
        {
            col.Item().AlignRight().Text("BORANG PCB/TP1 (1/2026)").FontSize(10).Bold();
            col.Item().PaddingTop(4).AlignCenter().Text("LEMBAGA HASIL DALAM NEGERI MALAYSIA").FontSize(9).Bold();
            col.Item().AlignCenter().Text(
                "BORANG TUNTUTAN POTONGAN DAN REBAT INDIVIDU BAGI TUJUAN POTONGAN CUKAI BULANAN (PCB)")
                .FontSize(7.5f).SemiBold();
            col.Item().AlignCenter().Text(
                "Kaedah-Kaedah Cukai Pendapatan (Potongan Daripada Saraan) 1994 · seksyen 152 Akta Cukai Pendapatan 1967")
                .FontSize(7).FontColor(PayrollPdfShared.Muted);
            col.Item().PaddingTop(6).Row(row =>
            {
                row.RelativeItem().Text($"BULAN POTONGAN: {MonthName(m.Month).ToUpperInvariant()}").FontSize(8).SemiBold();
                row.RelativeItem().AlignRight().Text($"TAHUN POTONGAN: {m.Year}").FontSize(8).SemiBold();
            });
        });

    private static void Body(IContainer container, Model m) =>
        container.Column(col =>
        {
            Band(col, "BAHAGIAN A: MAKLUMAT MAJIKAN");
            Kv(col, "A1", "Nama", m.EmployerName);
            Kv(col, "A2", "No. Pengenalan Cukai (TIN)", m.EmployerTin);

            Band(col, "BAHAGIAN B: MAKLUMAT INDIVIDU");
            Kv(col, "B1", "Nama", m.EmployeeName);
            Kv(col, "B2", "No. Kad Pengenalan / Pasport", m.IdNumber);
            Kv(col, "B3", "No. Pengenalan Cukai (TIN)", m.IncomeTaxNumber);

            Band(col, "BAHAGIAN C: MAKLUMAT POTONGAN");
            Table(col, m.Deductions, withLimit: true);

            // Kept whole, so a heading never ends a page with its rows on the next.
            col.Item().ShowEntire().Column(d =>
            {
                Band(d, "BAHAGIAN D: REBAT");
                Table(d, m.Rebates, withLimit: false);
            });

            // The declaration and the approval travel together.
            col.Item().ShowEntire().Column(ef =>
            {
                Band(ef, "BAHAGIAN E: AKUAN PEKERJA");
                ef.Item().PaddingVertical(3).Text(
                    "Saya mengakui bahawa semua maklumat yang dinyatakan dalam borang ini adalah benar, betul dan lengkap. "
                    + "Sekiranya maklumat yang diberikan tidak benar, tindakan mahkamah boleh diambil ke atas saya di bawah "
                    + "perenggan 113(1)(b) Akta Cukai Pendapatan 1967.").FontSize(7.5f);
                ef.Item().PaddingTop(10).Row(row =>
                {
                    row.RelativeItem().Text("Tarikh: ______________________").FontSize(8);
                    row.RelativeItem().AlignRight().Text("Tandatangan: ______________________").FontSize(8);
                });

                Band(ef, "BAHAGIAN F: PERSETUJUAN MAJIKAN");
                ef.Item().PaddingVertical(3).Text(
                    $"Permohonan tuntutan pekerja di atas adalah dipersetujui bagi potongan BULAN: "
                    + $"{MonthName(m.Month).ToUpperInvariant()}   TAHUN: {m.Year}").FontSize(8);
                Kv(ef, null, "Tarikh", m.ProcessedOn is { } d ? PayrollPdfShared.Date(d) : null);
                Kv(ef, null, "Nama", m.ProcessedByName);
                Kv(ef, null, "Jawatan", m.ProcessedByDesignation);
                Kv(ef, null, "Alamat majikan", m.EmployerAddress);
            });
        });

    private static void Table(ColumnDescriptor col, IReadOnlyList<Tp1Form.Row> rows, bool withLimit) =>
        col.Item().Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.ConstantColumn(30);
                c.RelativeColumn();
                if (withLimit) c.ConstantColumn(62);
                c.ConstantColumn(62);
                c.ConstantColumn(62);
            });

            table.Header(h =>
            {
                h.Cell().Element(HeadCell).Text("");
                h.Cell().Element(HeadCell).Text("");
                if (withLimit) h.Cell().Element(HeadCell).AlignRight().Text("HAD TAHUNAN (RM)");
                h.Cell().Element(HeadCell).AlignRight().Text("SEMASA (RM)");
                h.Cell().Element(HeadCell).AlignRight().Text("TERKUMPUL (RM)");
            });

            foreach (var r in rows)
            {
                var indent = r.IsPart ? 12f : 0f;
                table.Cell().Element(BodyCell).PaddingLeft(indent).Text(r.IsPart ? $"{r.Ref})" : r.Ref)
                    .FontSize(7.5f).Strong(!r.IsPart);
                table.Cell().Element(BodyCell).PaddingLeft(indent).Text(r.Label).FontSize(7.5f);
                if (withLimit)
                {
                    table.Cell().Element(BodyCell).AlignRight()
                        .Text(r.Limit is { } l ? PayrollPdfShared.Rm(l) : "").FontSize(7.5f)
                        .FontColor(r.IsPart ? PayrollPdfShared.Muted : PayrollPdfShared.Ink);
                }
                table.Cell().Element(BodyCell).AlignRight().Text(Amount(r.Current)).FontSize(7.5f).Strong(!r.IsPart);
                table.Cell().Element(BodyCell).AlignRight().Text(Amount(r.Cumulative)).FontSize(7.5f).Strong(!r.IsPart);
            }
        });

    private static TextBlockDescriptor Strong(this TextBlockDescriptor text, bool on) => on ? text.SemiBold() : text;

    // A blank rather than 0.00: the form's boxes are empty when nothing was
    // claimed, and a column of zeros hides the one line that matters.
    private static string Amount(decimal value) => value > 0m ? PayrollPdfShared.Rm(value) : "";

    private static IContainer HeadCell(IContainer c) =>
        c.BorderBottom(1).BorderColor(PayrollPdfShared.Rule).PaddingVertical(2)
            .DefaultTextStyle(t => t.FontSize(6.5f).SemiBold().FontColor(PayrollPdfShared.Muted));

    private static IContainer BodyCell(IContainer c) =>
        c.BorderBottom(0.5f).BorderColor("#e2e8f0").PaddingVertical(2);

    private static void Band(ColumnDescriptor col, string title) =>
        col.Item().PaddingTop(8).PaddingBottom(3).Background("#dbeafe").PaddingHorizontal(4).PaddingVertical(2)
            .Text(title).FontSize(7.5f).Bold();

    private static void Kv(ColumnDescriptor col, string? code, string label, string? value) =>
        col.Item().PaddingVertical(1.5f).Row(row =>
        {
            row.ConstantItem(30).Text(code ?? "").FontSize(7.5f).SemiBold();
            row.ConstantItem(150).Text(label).FontSize(7.5f);
            row.RelativeItem().Text(string.IsNullOrWhiteSpace(value) ? "—" : $": {value}").FontSize(8);
        });

    internal static string MonthName(int month) => month switch
    {
        1 => "Januari", 2 => "Februari", 3 => "Mac", 4 => "April", 5 => "Mei", 6 => "Jun",
        7 => "Julai", 8 => "Ogos", 9 => "September", 10 => "Oktober", 11 => "November", _ => "Disember",
    };
}
