using System.Text;
using AltomateHR.Api.Modules.Documents;
using AltomateHR.Api.Modules.Documents.Dtos;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AltomateHR.Api.Tests.Documents;

// "Upload Word file": a .docx / .txt / .md converted to template markup in
// memory. Fixtures are built here with OpenXml — no binary files committed.
public class DocxTemplateImportTests
{
    // ─── Word mapping ────────────────────────────────────────────────────

    [Fact]
    public void Headings_bold_and_bullets_map_to_markup()
    {
        var docx = Docx(body =>
        {
            body.Append(Styled("Title", R("Offer of Employment")));
            body.Append(Styled("Heading2", R("Terms")));
            body.Append(P(R("Dear "), R("Ali", bold: true), R(" bin Abu", bold: true), R(",")));
            body.Append(Listed(1, R("Salary of "), R("RM 4,500", bold: true)));
            body.Append(Listed(1, R("Annual leave")));
            body.Append(P(R("Yours sincerely")));
        });

        var result = DocxTemplateImport.Convert("offer.docx", docx);

        Assert.True(result.Ok, result.Error);
        Assert.Equal(
            "# Offer of Employment\n\n## Terms\n\nDear **Ali bin Abu**,\n\n- Salary of **RM 4,500**\n- Annual leave\n\nYours sincerely",
            result.Body);
        Assert.Equal("Offer of Employment", result.SuggestedName);
        Assert.DoesNotContain("****", result.Body);
    }

    [Fact]
    public void Numbered_items_become_bullets_with_a_warning()
    {
        var docx = Docx(body =>
        {
            body.Append(Listed(2, R("First")));
            body.Append(Listed(2, R("Second")));
        });

        var result = DocxTemplateImport.Convert("x.docx", docx);

        Assert.Equal("- First\n- Second", result.Body);
        Assert.Contains(result.Warnings, w => w.Contains("2 numbered list items became bullet points"));
    }

    [Fact]
    public void A_line_break_inside_a_paragraph_is_kept_and_bold_closes_at_it()
    {
        var docx = Docx(body =>
            body.Append(new Paragraph(
                R("Line one", bold: true), new Run(new Break()), R("Line two", bold: true))));

        var result = DocxTemplateImport.Convert("x.docx", docx);

        Assert.Equal("**Line one**\n**Line two**", result.Body);
    }

    [Fact]
    public void A_placeholder_split_across_runs_is_reassembled_without_bold_inside_it()
    {
        var docx = Docx(body =>
        {
            // Word's spell-checker and edits split one placeholder into pieces.
            body.Append(P(R("Dear "), R("{{employee.", bold: true), R("name}}"), R(",")));
            body.Append(P(R("{{", bold: true), R("company.name", bold: true), R("}}")));
            body.Append(P(R("IC: {{Employee "), R("IC}}")));
        });

        var result = DocxTemplateImport.Convert("x.docx", docx);

        Assert.Equal("Dear {{employee.name}},\n\n**{{company.name}}**\n\nIC: {{Employee IC}}", result.Body);
        Assert.Equal(["Employee IC"], DocxTemplateImport.UnknownPlaceholders(result.Body));
    }

    [Fact]
    public void A_word_mail_merge_field_becomes_a_placeholder()
    {
        var docx = Docx(body =>
            body.Append(new Paragraph(
                R("Dear "),
                new SimpleField(R("«EmployeeName»")) { Instruction = " MERGEFIELD EmployeeName " })));

        var result = DocxTemplateImport.Convert("x.docx", docx);

        Assert.Equal("Dear {{EmployeeName}}", result.Body);
        Assert.Contains(result.Warnings, w => w.Contains("mail-merge field"));
    }

    [Fact]
    public void A_table_is_flattened_to_one_paragraph_per_row()
    {
        var docx = Docx(body =>
        {
            body.Append(P(R("Details:")));
            body.Append(new Table(
                Row("Name", "{{employee.name}}"),
                Row("", ""),
                Row("Start date", "{{employee.joinDate}}")));
        });

        var result = DocxTemplateImport.Convert("x.docx", docx);

        Assert.Equal("Details:\n\nName | {{employee.name}}\n\nStart date | {{employee.joinDate}}", result.Body);
        Assert.Contains(result.Warnings, w => w.Contains("1 table was converted to text"));
    }

    [Fact]
    public void Images_are_skipped_and_counted()
    {
        var docx = Docx(body =>
        {
            body.Append(new Paragraph(new Run(new Drawing())));
            body.Append(P(R("Hello"), new Run(new Drawing())));
        });

        var result = DocxTemplateImport.Convert("x.docx", docx);

        Assert.True(result.Ok, result.Error);
        Assert.Equal("Hello", result.Body);
        Assert.Contains(result.Warnings, w => w.StartsWith("2 images were skipped"));
    }

    [Fact]
    public void Headers_are_skipped_with_a_warning()
    {
        var docx = Docx(body => body.Append(P(R("Body text"))), main =>
        {
            var header = main.AddNewPart<HeaderPart>();
            header.Header = new Header(P(R("ACME SDN BHD")));
        });

        var result = DocxTemplateImport.Convert("x.docx", docx);

        Assert.Equal("Body text", result.Body);
        Assert.Contains(result.Warnings, w => w.Contains("header and footer were skipped"));
    }

    [Fact]
    public void The_name_falls_back_to_the_file_name()
    {
        var docx = Docx(body => body.Append(P(R("Dear {{employee.name}}"))));

        var result = DocxTemplateImport.Convert("first_written-warning.docx", docx);

        Assert.Equal("first written warning", result.SuggestedName);
    }

    // ─── Refusals ────────────────────────────────────────────────────────

    [Fact]
    public void A_corrupt_word_file_is_refused()
    {
        var result = DocxTemplateImport.Convert("broken.docx", Encoding.UTF8.GetBytes("not a zip at all"));

        Assert.False(result.Ok);
        Assert.Equal(DocxTemplateImport.UnreadableWord, result.Error);
    }

    [Fact]
    public void Other_types_and_large_files_are_refused()
    {
        Assert.False(DocxTemplateImport.Convert("letter.pdf", [1, 2, 3]).Ok);
        Assert.Contains(".docx", DocxTemplateImport.Convert("letter.doc", [1, 2, 3]).Error);

        var big = DocxTemplateImport.Convert("big.txt", new byte[DocxTemplateImport.MaxBytes + 1]);
        Assert.False(big.Ok);
        Assert.Contains("5 MB", big.Error);
    }

    [Fact]
    public async Task The_endpoint_answers_400_for_an_unreadable_file()
    {
        var controller = new DocumentsController(new DocumentTemplateService(null!, null!), null!);

        var result = await controller.ImportTemplate(FormFile("broken.docx", Encoding.UTF8.GetBytes("garbage")));

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("couldn't be read", bad.Value!.ToString());
    }

    [Fact]
    public async Task The_endpoint_returns_the_converted_body_and_unknown_fields()
    {
        var controller = new DocumentsController(new DocumentTemplateService(null!, null!), null!);
        var docx = Docx(body => body.Append(P(R("Dear {{Employee Name}}, from {{company.name}}"))));

        var result = await controller.ImportTemplate(FormFile("Offer.docx", docx));

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<TemplateImportDto>(ok.Value);
        Assert.Equal("Offer", dto.SuggestedName);
        Assert.Equal(["Employee Name"], dto.UnknownFields);
    }

    // ─── Text / markdown ─────────────────────────────────────────────────

    [Fact]
    public void A_text_file_passes_through_with_line_endings_normalised()
    {
        var text = "﻿# Warning letter\r\n\r\nDear {{employee.name}},\r\n**Please** note\r\n";

        var result = DocxTemplateImport.Convert("warning.txt", Encoding.UTF8.GetBytes(text));

        Assert.True(result.Ok, result.Error);
        Assert.Equal("# Warning letter\n\nDear {{employee.name}},\n**Please** note", result.Body);
        Assert.Equal("Warning letter", result.SuggestedName);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void An_empty_text_file_is_refused()
    {
        Assert.False(DocxTemplateImport.Convert("empty.md", Encoding.UTF8.GetBytes("\r\n  \n")).Ok);
    }

    // ─── Fixture builders ────────────────────────────────────────────────

    private static byte[] Docx(Action<Body> build, Action<MainDocumentPart>? extra = null)
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.AddNewPart<StyleDefinitionsPart>().Styles = new Styles(
                ParagraphStyle("Title", "Title"),
                ParagraphStyle("Heading1", "heading 1"),
                ParagraphStyle("Heading2", "heading 2"));
            main.AddNewPart<NumberingDefinitionsPart>().Numbering = new Numbering(
                AbstractList(1, NumberFormatValues.Bullet),
                AbstractList(2, NumberFormatValues.Decimal),
                new NumberingInstance(new AbstractNumId { Val = 1 }) { NumberID = 1 },
                new NumberingInstance(new AbstractNumId { Val = 2 }) { NumberID = 2 });

            var body = new Body();
            build(body);
            main.Document = new Document(body);
            extra?.Invoke(main);
        }
        return stream.ToArray();
    }

    private static Style ParagraphStyle(string id, string name) =>
        new(new StyleName { Val = name }) { Type = StyleValues.Paragraph, StyleId = id };

    private static AbstractNum AbstractList(int id, NumberFormatValues format) =>
        new(new Level(new NumberingFormat { Val = format }) { LevelIndex = 0 }) { AbstractNumberId = id };

    private static Run R(string text, bool bold = false)
    {
        var run = new Run();
        if (bold) run.Append(new RunProperties(new Bold()));
        run.Append(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        return run;
    }

    private static Paragraph P(params OpenXmlElement[] runs) => new(runs);

    private static Paragraph Styled(string styleId, params OpenXmlElement[] runs)
    {
        var p = new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = styleId }));
        p.Append(runs);
        return p;
    }

    private static Paragraph Listed(int numId, params OpenXmlElement[] runs)
    {
        var p = new Paragraph(new ParagraphProperties(
            new NumberingProperties(new NumberingLevelReference { Val = 0 }, new NumberingId { Val = numId })));
        p.Append(runs);
        return p;
    }

    private static TableRow Row(params string[] cells) =>
        new(cells.Select(c => new TableCell(P(R(c)))));

    private static IFormFile FormFile(string name, byte[] content) =>
        new FormFile(new MemoryStream(content), 0, content.Length, "file", name);
}
