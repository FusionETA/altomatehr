using System.Text;
using AltomateHR.Api.Common.Tabular;

namespace AltomateHR.Api.Tests.Common;

// PDF is the write-only third format. A byte-level assertion on a rendered
// document would be brittle and meaningless, so these pin the things that
// actually matter: it renders at all, it's a real PDF, the edge cases don't
// throw, and — most importantly — nothing tries to READ one back.
public class TabularPdfTests
{
    private static TabularSheet Sheet(int rows = 3)
    {
        var sheet = new TabularSheet("Claims", ["Claim #", "Employee", "Amount"], "Jan 2026 · 3 claims");
        for (var i = 0; i < rows; i++)
            sheet.AddRow($"CLM-{i}", "Ahmad Ali", $"{(i + 1) * 10}.00");
        return sheet;
    }

    private static TabularPdfHeader Header() => new("Acme Sdn Bhd", "Claims Report");

    [Fact]
    public void RendersARealPdf()
    {
        var bytes = TabularWriter.Write(Sheet(), TabularFormat.Pdf, Header());

        // "%PDF" — the magic number every reader checks first.
        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes.Take(4).ToArray()));
        Assert.True(bytes.Length > 1_000);
    }

    [Fact]
    public void RendersAnEmptySheetWithoutThrowing()
    {
        // An admin filtering down to nothing must get a PDF saying so, not a 500.
        var bytes = TabularWriter.Write(Sheet(rows: 0), TabularFormat.Pdf, Header());

        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes.Take(4).ToArray()));
    }

    [Fact]
    public void RendersWithNoSheetsAtAll()
    {
        var bytes = TabularWriter.Write(Array.Empty<TabularSheet>(), TabularFormat.Pdf, Header());

        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes.Take(4).ToArray()));
    }

    // A missing masthead shouldn't cost somebody their download.
    [Fact]
    public void RendersWithoutAHeader()
    {
        var bytes = TabularWriter.Write(Sheet(), TabularFormat.Pdf);

        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes.Take(4).ToArray()));
    }

    [Fact]
    public void RendersEveryRowOfAMultiPageTable()
    {
        // 400 rows spills well past one page; the renderer repeats the header on
        // each, so this mostly proves pagination doesn't blow up.
        var big = new TabularSheet("Big", ["A", "B"]);
        for (var i = 0; i < 400; i++) big.AddRow($"row-{i}", "value");

        var bytes = TabularWriter.Write(big, TabularFormat.Pdf, Header());

        Assert.True(bytes.Length > 5_000);
    }

    [Fact]
    public void RendersMultipleSheets()
    {
        var bytes = TabularWriter.Write(
            [Sheet(), new TabularSheet("Daily Records", ["Date", "Hours"]).AddRow("2026-01-05", "7.50")],
            TabularFormat.Pdf,
            Header());

        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes.Take(4).ToArray()));
    }

    // ---- The totals row ----

    [Fact]
    public void TotalsAppearInCsvAndXlsxAsTheFinalRow()
    {
        var sheet = Sheet();
        sheet.SetTotals("3 claim(s)", "TOTAL", "60.00");

        var csv = Encoding.UTF8.GetString(TabularWriter.Write(sheet, TabularFormat.Csv)).TrimStart('﻿');
        Assert.EndsWith("3 claim(s),TOTAL,60.00\n", csv);

        var xlsxRows = TabularReader.Read(
            TabularWriter.Write(sheet, TabularFormat.Xlsx), TabularFormat.Xlsx);
        Assert.Equal(["3 claim(s)", "TOTAL", "60.00"], xlsxRows[^1]);
    }

    [Fact]
    public void TotalsAreNotCountedAsDataRows()
    {
        var sheet = Sheet();
        sheet.SetTotals("3 claim(s)", "TOTAL", "60.00");

        // The distinction is what lets the PDF rule the line off, and what stops
        // anything downstream from reading "TOTAL" as an employee.
        Assert.Equal(3, sheet.Rows.Count);
        Assert.NotNull(sheet.TotalsRow);
    }

    // ---- PDF is export-only ----

    [Theory]
    [InlineData("pdf", TabularFormat.Pdf)]
    [InlineData("PDF", TabularFormat.Pdf)]
    [InlineData("xlsx", TabularFormat.Xlsx)]
    [InlineData("excel", TabularFormat.Xlsx)]
    [InlineData("csv", TabularFormat.Csv)]
    [InlineData("nonsense", TabularFormat.Csv)]
    [InlineData(null, TabularFormat.Csv)]
    public void ParsesTheFormatQueryParam(string? value, TabularFormat expected)
    {
        Assert.Equal(expected, TabularFormats.Parse(value));
    }

    [Fact]
    public void PdfIsNotImportable()
    {
        Assert.False(TabularFormat.Pdf.IsImportable());
        Assert.True(TabularFormat.Csv.IsImportable());
        Assert.True(TabularFormat.Xlsx.IsImportable());
    }

    // The upload path must never route a PDF to a parser: the CSV reader would
    // happily turn binary into "rows" rather than failing.
    [Theory]
    [InlineData("report.pdf", "application/pdf")]
    [InlineData("report.pdf", null)]
    [InlineData("scan.png", "image/png")]
    public void DetectRefusesAnUploadThatIsNotASpreadsheet(string fileName, string? contentType)
    {
        Assert.Null(TabularFormats.Detect(fileName, contentType));
    }

    [Fact]
    public void ReadingAPdfIsRefusedRatherThanParsed()
    {
        var pdf = TabularWriter.Write(Sheet(), TabularFormat.Pdf, Header());

        var error = Assert.Throws<InvalidDataException>(
            () => TabularReader.Read(pdf, TabularFormat.Pdf));
        Assert.Contains("export format only", error.Message);
    }

    [Fact]
    public void PdfCarriesItsOwnContentTypeAndExtension()
    {
        Assert.Equal("application/pdf", TabularFormat.Pdf.ContentType());
        Assert.Equal("pdf", TabularFormat.Pdf.Extension());

        var result = TabularExportResult.From(Sheet(), TabularFormat.Pdf, "claims-summary", Header());
        Assert.Equal("claims-summary.pdf", result.FileName);
        Assert.Equal("application/pdf", result.ContentType);
    }
}
