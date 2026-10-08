using AltomateHR.Api.Modules.Payroll.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AltomateHR.Api.Modules.Documents.Pdf;

// Everything the letter prints, already resolved to text.
public sealed record LetterPdfModel(
    string Title,
    string CompanyName,
    string? RegistrationNo,
    string? CompanyAddressLine,   // one line, for the letterhead
    string? CompanyPhone,
    string? CompanyEmail,
    string DateText,
    IReadOnlyList<LetterMarkup.Block> Blocks,
    string SignatoryName,
    string SignatoryPosition,
    // Printed across the top in preview so a sample can't be mistaken for a
    // real letter. Null on a generated letter.
    string? Watermark = null);

// A company letter on A4: letterhead, date, the template body, signature block.
public static class LetterPdf
{
    private const string Ink = "#0f172a";
    private const string Muted = "#64748b";
    private const string Rule = "#cbd5e1";
    private const string Flag = "#b45309";

    public static byte[] Render(LetterPdfModel m) =>
        Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.MarginVertical(42);
            page.MarginHorizontal(56);
            page.DefaultTextStyle(t => t.FontFamily(PdfFont.Family).FontSize(10.5f).FontColor(Ink).LineHeight(1.35f));

            page.Header().Element(h => Letterhead(h, m));
            page.Content().PaddingTop(18).Column(col =>
            {
                if (!string.IsNullOrWhiteSpace(m.Watermark))
                {
                    col.Item().PaddingBottom(10).Background("#fef3c7").Padding(6)
                        .Text(m.Watermark).FontSize(8.5f).SemiBold().FontColor(Flag);
                }

                col.Item().PaddingBottom(14).Text(m.DateText);

                foreach (var block in m.Blocks) Block(col, block);

                Signature(col, m);
            });
            page.Footer().AlignRight().Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(7.5f).FontColor(Muted));
                t.Span("Page ");
                t.CurrentPageNumber();
                t.Span(" of ");
                t.TotalPages();
            });
        }))
        .WithMetadata(new DocumentMetadata { Title = m.Title, Creator = "AltomateHR" })
        .GeneratePdf();

    private static void Letterhead(IContainer container, LetterPdfModel m) =>
        container.BorderBottom(1).BorderColor(Rule).PaddingBottom(8).Column(col =>
        {
            col.Item().Text(t =>
            {
                t.Span(m.CompanyName).FontSize(15).Bold();
                if (!string.IsNullOrWhiteSpace(m.RegistrationNo))
                    t.Span($"   Reg. No. {m.RegistrationNo}").FontSize(8.5f).FontColor(Muted);
            });

            if (!string.IsNullOrWhiteSpace(m.CompanyAddressLine))
                col.Item().PaddingTop(2).Text(m.CompanyAddressLine).FontSize(8.5f).FontColor(Muted);

            var contact = string.Join("   ·   ", new[]
            {
                string.IsNullOrWhiteSpace(m.CompanyPhone) ? null : $"Tel: {m.CompanyPhone}",
                string.IsNullOrWhiteSpace(m.CompanyEmail) ? null : $"Email: {m.CompanyEmail}",
            }.Where(s => s is not null));
            if (contact.Length > 0)
                col.Item().Text(contact).FontSize(8.5f).FontColor(Muted);
        });

    private static void Block(ColumnDescriptor col, LetterMarkup.Block block)
    {
        switch (block.Kind)
        {
            case LetterMarkup.BlockKind.Heading1:
                col.Item().PaddingTop(4).PaddingBottom(8).Text(t => Spans(t, block.Lines[0], size: 13, forceBold: true));
                break;
            case LetterMarkup.BlockKind.Heading2:
                col.Item().PaddingTop(2).PaddingBottom(6).Text(t => Spans(t, block.Lines[0], size: 11.5f, forceBold: true));
                break;
            case LetterMarkup.BlockKind.Bullets:
                col.Item().PaddingBottom(10).Column(list =>
                {
                    foreach (var item in block.Lines)
                    {
                        list.Item().PaddingBottom(3).Row(row =>
                        {
                            row.ConstantItem(16).Text("•");
                            row.RelativeItem().Text(t => Spans(t, item));
                        });
                    }
                });
                break;
            default:
                col.Item().PaddingBottom(10).Text(t =>
                {
                    for (var i = 0; i < block.Lines.Count; i++)
                    {
                        if (i > 0) t.Span("\n");
                        Spans(t, block.Lines[i]);
                    }
                });
                break;
        }
    }

    private static void Spans(TextDescriptor t, IReadOnlyList<LetterMarkup.Span> spans, float? size = null, bool forceBold = false)
    {
        foreach (var s in spans)
        {
            var span = t.Span(s.Text);
            if (size is { } px) span.FontSize(px);
            if (s.Bold || forceBold) span.Bold();
        }
    }

    private static void Signature(ColumnDescriptor col, LetterPdfModel m) =>
        col.Item().PaddingTop(16).ShowEntire().Column(sig =>
        {
            sig.Item().Text("Yours sincerely,");
            sig.Item().Text($"for and on behalf of {m.CompanyName}").FontColor(Muted).FontSize(9.5f);
            sig.Item().PaddingTop(42).Width(190).BorderTop(1).BorderColor(Ink).PaddingTop(4)
                .Text(string.IsNullOrWhiteSpace(m.SignatoryName) ? " " : m.SignatoryName).Bold();
            if (!string.IsNullOrWhiteSpace(m.SignatoryPosition))
                sig.Item().Text(m.SignatoryPosition);
        });
}
