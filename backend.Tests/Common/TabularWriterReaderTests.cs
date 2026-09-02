using System.Text;
using AltomateHR.Api.Common.Tabular;

namespace AltomateHR.Api.Tests.Common;

// The CSV/XLSX layer every import and export in the app goes through. Its whole
// job is that a file written here reads back identically — so most of these are
// round-trip tests rather than string assertions.
public class TabularWriterReaderTests
{
    private static TabularSheet Sheet(params string[][] rows)
    {
        var sheet = new TabularSheet("People", ["Name", "Email"]);
        foreach (var row in rows) sheet.AddRow(row);
        return sheet;
    }

    private static string Csv(TabularSheet sheet) =>
        Encoding.UTF8.GetString(TabularWriter.Write(sheet, TabularFormat.Csv))
            .TrimStart('﻿');   // drop the Excel BOM for assertions

    [Fact]
    public void CsvWritesTheHeaderThenOneLinePerRow()
    {
        var csv = Csv(Sheet(["Ada", "ada@x.com"], ["Alan", "alan@x.com"]));
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("Name,Email", lines[0]);
        Assert.Equal(3, lines.Length);            // header + 2 rows
        Assert.Equal("Ada,ada@x.com", lines[1]);
    }

    [Fact]
    public void CsvQuotesValuesContainingCommas()
    {
        Assert.Contains("\"Lovelace, Ada\"", Csv(Sheet(["Lovelace, Ada", "ada@x.com"])));
    }

    [Fact]
    public void CsvEscapesEmbeddedQuotesByDoublingThem()
    {
        Assert.Contains("\"The \"\"Big\"\" One\"", Csv(Sheet(["The \"Big\" One", ""])));
    }

    [Fact]
    public void CsvStartsWithAUtf8Bom_SoExcelReadsNonAsciiNames()
    {
        var bytes = TabularWriter.Write(Sheet(["Ali", "ali@x.com"]), TabularFormat.Csv);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes.Take(3).ToArray());
    }

    // The property that actually matters: an exported file must survive a
    // re-import. Commas, quotes and newlines are the three characters that
    // historically break hand-rolled CSV.
    [Theory]
    [InlineData(TabularFormat.Csv)]
    [InlineData(TabularFormat.Xlsx)]
    public void RoundTripsAwkwardValues(TabularFormat format)
    {
        var written = TabularWriter.Write(
            Sheet(["Lovelace, Ada", "a\"b"], ["line\none", "x@y.com"]),
            format);

        var rows = TabularReader.Read(written, format);

        Assert.Equal(3, rows.Count);
        Assert.Equal(["Name", "Email"], rows[0]);
        Assert.Equal(["Lovelace, Ada", "a\"b"], rows[1]);
        Assert.Equal(["line\none", "x@y.com"], rows[2]);
    }

    [Fact]
    public void ReaderDropsWhollyBlankRows_ButKeepsPartiallyEmptyOnes()
    {
        var rows = TabularReader.Read(
            Encoding.UTF8.GetBytes("Name,Email\nAda,\n,\n\nAlan,alan@x.com\n"),
            TabularFormat.Csv);

        Assert.Equal(3, rows.Count);              // header, Ada, Alan
        Assert.Equal("Ada", rows[1][0]);
        Assert.Equal("Alan", rows[2][0]);
    }

    [Fact]
    public void ReaderHandlesCrlfAndAMissingTrailingNewline()
    {
        var rows = TabularReader.Read(
            Encoding.UTF8.GetBytes("Name,Email\r\nAda,ada@x.com"),
            TabularFormat.Csv);

        Assert.Equal(2, rows.Count);
        Assert.Equal(["Ada", "ada@x.com"], rows[1]);
    }

    [Fact]
    public void ReaderRejectsSomethingThatIsNotAWorkbook()
    {
        Assert.Throws<InvalidDataException>(() =>
            TabularReader.Read(Encoding.UTF8.GetBytes("this is not a workbook"), TabularFormat.Xlsx));
    }

    [Fact]
    public void MultiSheetCsvLabelsEachSheet_BecauseCsvCannotExpressTabs()
    {
        var csv = Encoding.UTF8.GetString(TabularWriter.Write(
            [new TabularSheet("First", ["A"]), new TabularSheet("Second", ["B"])],
            TabularFormat.Csv)).TrimStart('﻿');

        Assert.Contains("# First", csv);
        Assert.Contains("# Second", csv);
    }

    [Fact]
    public void SheetNamesAreTruncatedAndStrippedForExcel()
    {
        // Excel caps names at 31 chars and forbids these characters; handing them
        // straight to ClosedXML throws.
        var sheet = new TabularSheet("Report: 2026/01 [draft] with a very long tail", ["A"]);

        Assert.DoesNotContain(':', sheet.Name);
        Assert.DoesNotContain('/', sheet.Name);
        Assert.True(sheet.Name.Length <= 31);
    }
}
