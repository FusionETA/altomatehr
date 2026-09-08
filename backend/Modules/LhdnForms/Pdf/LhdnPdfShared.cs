using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AltomateHR.Api.Modules.LhdnForms.Pdf;

// Shared layout building blocks for the 5 LHDN form PDFs — one clean-statement
// interpretation of the official forms, not a boxed grid replica. Mirrors
// Leave/LeaveSummaryPdf.cs's conventions (static helpers, hex-string colours,
// a Frame/Header/Footer split per page).
internal static class LhdnPdfShared
{
    private const string Ink = "#0f172a";
    private const string Muted = "#64748b";
    private const string Faint = "#94a3b8";
    private const string Rule = "#cbd5e1";
    private const string PanelBg = "#f8fafc";
    private const string Warn = "#b45309";
    private const string WarnBg = "#fffbeb";

    public static readonly string[] MonthNames =
        ["January", "February", "March", "April", "May", "June",
         "July", "August", "September", "October", "November", "December"];

    public static readonly IReadOnlyDictionary<string, string> MaritalStatusLabels = new Dictionary<string, string>
    {
        ["SINGLE"] = "Single / Bujang",
        ["MARRIED"] = "Married / Berkahwin",
        ["DIVORCED"] = "Divorced / Bercerai",
        ["WIDOWED"] = "Widowed / Balu",
    };

    public static readonly IReadOnlyDictionary<string, string> GenderLabels = new Dictionary<string, string>
    {
        ["MALE"] = "Male / Lelaki",
        ["FEMALE"] = "Female / Perempuan",
    };

    public static void Frame(PageDescriptor page)
    {
        page.Size(PageSizes.A4);
        page.MarginVertical(36);
        page.MarginHorizontal(40);
        page.DefaultTextStyle(t => t.FontFamily("Helvetica").FontSize(9).FontColor(Ink));
    }

    public static string FmtRm(decimal? v) =>
        v is null ? "" : v.Value.ToString("#,##0.00", System.Globalization.CultureInfo.InvariantCulture);

    public static string FmtDate(DateTime? d) => d is null ? "" : d.Value.ToString("d MMM yyyy");

    public static void BrandHeader(IContainer container, LhdnFormPayload payload, string formCode, string formCodeSub) =>
        container.PaddingBottom(8).BorderBottom(1).BorderColor(Rule).Row(row =>
        {
            var employer = payload.Employer;
            row.RelativeItem(4).Column(left =>
            {
                left.Item().Text(employer.EmployerName ?? payload.OrganizationName).FontSize(12).Bold();
                if (!string.IsNullOrWhiteSpace(employer.FullAddress))
                    left.Item().Text(employer.FullAddress).FontSize(8.5f).FontColor(Muted);
                left.Item().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(8.5f).FontColor(Muted));
                    t.Span(!string.IsNullOrWhiteSpace(employer.EmployerTin)
                        ? $"Employer No. (E): {employer.EmployerTin}"
                        : "Employer No. (E): —");
                    if (!string.IsNullOrWhiteSpace(employer.Phone)) t.Span($"   Tel: {employer.Phone}");
                    if (!string.IsNullOrWhiteSpace(employer.Email)) t.Span($"   Email: {employer.Email}");
                });
            });
            row.RelativeItem(2).Column(right =>
            {
                right.Item().AlignRight().Text(formCode).FontSize(11).Bold();
                right.Item().AlignRight().Text(formCodeSub).FontSize(8.5f).FontColor(Muted);
            });
        });

    /// First line is the bold 13pt title; every line after is a muted 9pt subtitle.
    public static void TitleBlock(IContainer container, params string[] lines) =>
        container.PaddingVertical(12).Column(col =>
        {
            for (var i = 0; i < lines.Length; i++)
            {
                if (i == 0)
                    col.Item().AlignCenter().Text(lines[i]).FontSize(13).Bold().LetterSpacing(0.02f);
                else
                    col.Item().PaddingTop(i == 1 ? 0 : 4).AlignCenter().Text(lines[i]).FontSize(9).FontColor(Muted);
            }
        });

    public static void SectionHeader(IContainer container, string text) =>
        container.PaddingTop(12).PaddingBottom(6).Background(PanelBg)
            .PaddingVertical(3).PaddingHorizontal(6).Text(text).FontSize(10).Bold();

    /// Label flex 4, bold value flex 6 — "—" when the value is blank.
    public static void KvRow(IContainer container, string label, string? value) =>
        container.PaddingVertical(1.5f).Row(row =>
        {
            row.RelativeItem(4).Text(label).FontSize(9);
            row.RelativeItem(6).Text(string.IsNullOrWhiteSpace(value) ? "—" : value).FontSize(9).Bold();
        });

    /// Two label/value pairs on one row — each label flex 2, value flex 3.
    public static void KvRowDual(
        IContainer container, string labelLeft, string? valueLeft, string labelRight, string? valueRight) =>
        container.PaddingVertical(1.5f).Row(row =>
        {
            row.RelativeItem(2).Text(labelLeft).FontSize(9);
            row.RelativeItem(3).Text(string.IsNullOrWhiteSpace(valueLeft) ? "—" : valueLeft).FontSize(9).Bold();
            row.RelativeItem(2).Text(labelRight).FontSize(9);
            row.RelativeItem(3).Text(string.IsNullOrWhiteSpace(valueRight) ? "—" : valueRight).FontSize(9).Bold();
        });

    /// Label flex 5, right-aligned RM amount flex 2 — blank (not "—") when the
    /// amount is null, since a blank here means "not tracked", not "zero".
    public static void AmtRow(IContainer container, string label, decimal? amount) =>
        container.PaddingVertical(1.5f).Row(row =>
        {
            row.RelativeItem(5).Text(label).FontSize(9);
            row.RelativeItem(2).AlignRight().Text(FmtRm(amount)).FontSize(9);
        });

    /// Same as AmtRow, bold with a top rule — the "TOTAL" row under a group.
    public static void TotalAmtRow(IContainer container, string label, decimal amount) =>
        container.PaddingTop(3).BorderTop(0.5f).BorderColor(Ink).PaddingVertical(2).Row(row =>
        {
            row.RelativeItem(5).Text(label).FontSize(9).Bold();
            row.RelativeItem(2).AlignRight().Text(FmtRm(amount)).FontSize(9).Bold();
        });

    /// Inline "[X] Label   [ ] Label   [ ] Label" pseudo-checkboxes.
    public static void CheckOptions(IContainer container, params (string Label, bool Selected)[] options) =>
        container.PaddingVertical(1.5f).Row(row =>
        {
            foreach (var (label, selected) in options)
                row.AutoItem().PaddingRight(14).Text($"{(selected ? "[X]" : "[ ]")} {label}").FontSize(9);
        });

    public static string? JoinAddress(params string?[] parts)
    {
        var present = parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        return present.Length == 0 ? null : string.Join(", ", present);
    }

    public static void SignatureBlock(
        IContainer container, (string Label, string? Value)[] left, (string Label, string? Value)[] right) =>
        container.PaddingTop(22).Row(row =>
        {
            row.RelativeItem().PaddingRight(12).Column(col => SignatureColumn(col, left));
            row.RelativeItem().Column(col => SignatureColumn(col, right));
        });

    private static void SignatureColumn(ColumnDescriptor col, (string Label, string? Value)[] rows)
    {
        for (var i = 0; i < rows.Length; i++)
        {
            var (label, value) = rows[i];
            col.Item().PaddingTop(i == 0 ? 0 : 6).Text(label).FontSize(8.5f).FontColor(Muted);
            col.Item().PaddingTop(36).BorderTop(0.5f).BorderColor(Ink)
                .PaddingTop(4).Text(value ?? "").FontSize(8.5f);
        }
    }

    public static void ClosingNote(IContainer container, string text) =>
        container.PaddingTop(14).Text(text).FontSize(8.5f).FontColor(Muted).LineHeight(1.4f);

    public static void Footer(IContainer container, string formCode, string employerLabel, string employeeLabel) =>
        container.Row(row =>
        {
            row.RelativeItem().Text($"{formCode} · {employerLabel} · {employeeLabel}").FontSize(7.5f).FontColor(Faint);
            row.RelativeItem().AlignRight().Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(7.5f).FontColor(Faint));
                t.CurrentPageNumber();
                t.Span(" / ");
                t.TotalPages();
            });
        });

    /// The four YTD-dependent forms (everything but CP22) show this when
    /// there's no payroll-run engine to source real figures from — every
    /// YTD/monthly amount on the form is blank or zero, and this says so
    /// plainly rather than letting a real-looking "0.00" pass as confirmed.
    public static void PayrollHistoryDisclaimer(IContainer container) =>
        container.PaddingBottom(10).Background(WarnBg).Padding(8).Text(
            "This organisation has not yet run payroll in AltomateHR, so every year-to-date and monthly figure " +
            "below is blank or zero — not a confirmed amount. Source the real figures from your previous payroll " +
            "records before submitting this form.").FontSize(8.5f).FontColor(Warn).Bold();
}
