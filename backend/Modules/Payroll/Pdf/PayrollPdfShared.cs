using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AltomateHR.Api.Modules.Payroll.Pdf;

// Shared layout for the payroll documents, following the same conventions as
// Modules/LhdnForms/Pdf/LhdnPdfShared — static helpers, hex colours, one Frame
// per page.
internal static class PayrollPdfShared
{
    public const string Ink = "#0f172a";
    public const string Muted = "#64748b";
    public const string Faint = "#94a3b8";
    public const string Rule = "#cbd5e1";
    public const string PanelBg = "#f8fafc";
    public const string Accent = "#0f172a";

    public static void Frame(PageDescriptor page)
    {
        page.Size(PageSizes.A4);
        page.MarginVertical(36);
        page.MarginHorizontal(40);
        page.DefaultTextStyle(t => t.FontFamily(PdfFont.Family).FontSize(9).FontColor(Ink));
    }

    // Money on a payslip is read by someone checking it against their bank
    // statement, so it is grouped and always carries both decimals. Invariant
    // culture: the server's locale must not decide where the comma goes.
    public static string Rm(decimal value) =>
        value.ToString("#,##0.00", System.Globalization.CultureInfo.InvariantCulture);

    public static string Date(DateTime value) => value.ToString("d MMM yyyy");

    // A labelled fact in a two-column identity grid.
    public static void Field(ColumnDescriptor column, string label, string? value) =>
        column.Item().PaddingBottom(3).Row(row =>
        {
            row.ConstantItem(78).Text(label).FontSize(7.5f).FontColor(Muted);
            row.RelativeItem().Text(string.IsNullOrWhiteSpace(value) ? "—" : value).FontSize(8.5f);
        });

    // A label/amount pair — the unit every earnings and deductions column is
    // built from.
    public static void AmountRow(ColumnDescriptor column, string label, decimal amount) =>
        column.Item().PaddingVertical(1.5f).Row(row =>
        {
            row.RelativeItem().Text(label).FontSize(8.5f);
            row.ConstantItem(80).AlignRight().Text(Rm(amount)).FontSize(8.5f);
        });

    public static void TotalRow(ColumnDescriptor column, string label, decimal amount) =>
        column.Item().PaddingTop(4).BorderTop(1).BorderColor(Rule).PaddingTop(3).Row(row =>
        {
            row.RelativeItem().Text(label).FontSize(8.5f).SemiBold();
            row.ConstantItem(80).AlignRight().Text(Rm(amount)).FontSize(8.5f).SemiBold();
        });

    public static void SectionLabel(ColumnDescriptor column, string text) =>
        column.Item().PaddingBottom(3).Text(text.ToUpperInvariant())
            .FontSize(7.5f).SemiBold().FontColor(Muted).LetterSpacing(0.06f);

    public static void Footer(IContainer container, string note) =>
        container.PaddingTop(6).BorderTop(1).BorderColor(Rule).PaddingTop(4).Row(row =>
        {
            row.RelativeItem().Text(note).FontSize(7).FontColor(Faint);
            row.ConstantItem(70).AlignRight().Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(7).FontColor(Faint));
                t.CurrentPageNumber();
                t.Span(" / ");
                t.TotalPages();
            });
        });

    // See PayslipPdf.MaskedAccount, which is the public face of this.
    public static string MaskedAccount(string? bankName, string? accountNumber)
    {
        var digits = (accountNumber ?? string.Empty).Trim();
        if (digits.Length == 0) return string.IsNullOrWhiteSpace(bankName) ? "—" : bankName!;

        var tail = digits.Length <= 4 ? digits : digits[^4..];
        var masked = $"•••• {tail}";

        return string.IsNullOrWhiteSpace(bankName) ? masked : $"{bankName} {masked}";
    }
}
