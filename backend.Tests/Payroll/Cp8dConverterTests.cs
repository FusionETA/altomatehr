using System.IO.Compression;
using System.Text;
using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Dtos;

namespace AltomateHR.Api.Tests.Payroll;

// The CP8D converter: hand-entered rows in, the zipped M + P pair out.
//
// The point of routing this through Cp8dTxt rather than rendering in the
// browser is that a converted file and a generated one are the SAME bytes for
// the same figures. These tests pin that: same column count, same order, and a
// category the admin typed rather than one derived from a profile that isn't
// there.
public class Cp8dConverterTests
{
    private const int ExpectedColumns = 16;

    private static Cp8dConvertRowDto Row(
        string name = "Ahmad Bin Ali",
        string taxRef = "SG 12345678",
        string ic = "900101-01-5523",
        string category = "2",
        bool taxBorne = false,
        int children = 2,
        decimal childRelief = 4000m,
        decimal gross = 72000m,
        decimal epf = 7920m,
        decimal pcb = 1234.56m) =>
        new()
        {
            Name = name,
            TaxRef = taxRef,
            NewIc = ic,
            Category = category,
            TaxBorneByEmployer = taxBorne,
            Children = children,
            ChildRelief = childRelief,
            AnnualGross = gross,
            Epf = epf,
            Pcb = pcb,
        };

    private static Cp8dConvertRequestDto Request(params Cp8dConvertRowDto[] rows) =>
        new()
        {
            EmployerNo = "E 1234567890",
            EmployerName = "Globe Engineering Sdn Bhd",
            Year = 2026,
            Employees = rows.Length == 0 ? [Row()] : [.. rows],
        };

    private static PayrollAnnualReportService Service() =>
        new(null!, null!, null!, null!, null!, null!);

    private static Dictionary<string, string> Unzip(byte[] content)
    {
        using var archive = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read);
        return archive.Entries.ToDictionary(
            e => e.Name,
            e =>
            {
                using var reader = new StreamReader(e.Open(), Encoding.ASCII);
                return reader.ReadToEnd();
            },
            StringComparer.Ordinal);
    }

    [Fact]
    public void TheZipCarriesTheMAndPPairNamedForTheEmployerAndYear()
    {
        var result = Service().ConvertCp8d(Request());

        Assert.True(result.Ok, result.Error);
        Assert.Equal("CP8D_1234567890_2026.zip", result.FileName);

        var files = Unzip(result.Content!);
        Assert.Equal(2, files.Count);
        Assert.Contains("M1234567890_2026.TXT", files.Keys);
        Assert.Contains("P1234567890_2026.TXT", files.Keys);
    }

    [Fact]
    public void TheEmployerRecordIsRenderedFromWhatWasTypedIn()
    {
        var files = Unzip(Service().ConvertCp8d(Request()).Content!);

        Assert.Equal("1234567890|GLOBE ENGINEERING SDN BHD|2026\r\n", files["M1234567890_2026.TXT"]);
    }

    [Fact]
    public void AnEmployeeRowKeepsTheSixteenColumnContract()
    {
        var files = Unzip(Service().ConvertCp8d(Request()).Content!);
        var line = files["P1234567890_2026.TXT"].Split("\r\n")[0];

        // Trailing pipe, so splitting yields one more part than there are columns.
        Assert.Equal(ExpectedColumns + 1, line.Split('|').Length);

        var cols = line.Split('|');
        Assert.Equal("AHMAD BIN ALI", cols[0]);    // 1  name, uppercased
        Assert.Equal("12345678", cols[1]);         // 2  tax ref, digits only
        Assert.Equal("900101015523", cols[2]);     // 3  IC, dashes stripped
        Assert.Equal("2", cols[3]);                // 4  category, as typed
        Assert.Equal("2", cols[4]);                // 5  tax borne by employer: no
        Assert.Equal("2", cols[5]);                // 6  qualifying children
        Assert.Equal("4000", cols[6]);             // 7  child relief, whole ringgit
        Assert.Equal("72000", cols[7]);            // 8  annual gross, whole ringgit
        Assert.Equal("7920", cols[13]);            // 14 EPF, whole ringgit
        Assert.Equal("1234.56", cols[15]);         // 16 PCB, two decimals
    }

    // The whole reason for the override: there is no profile behind a typed
    // row, so the category cannot be derived from marital status and children
    // the way a real payload's is.
    [Theory]
    [InlineData("1")]
    [InlineData("2")]
    [InlineData("3")]
    public void TheTypedCategoryIsTheOneThatShipsRatherThanADerivedOne(string category)
    {
        var files = Unzip(Service().ConvertCp8d(Request(Row(category: category))).Content!);
        var cols = files["P1234567890_2026.TXT"].Split("\r\n")[0].Split('|');

        Assert.Equal(category, cols[3]);
    }

    [Fact]
    public void TaxBorneByTheEmployerFlipsColumnFive()
    {
        var files = Unzip(Service().ConvertCp8d(Request(Row(taxBorne: true))).Content!);
        var cols = files["P1234567890_2026.TXT"].Split("\r\n")[0].Split('|');

        Assert.Equal("1", cols[4]);
    }

    [Fact]
    public void EveryRowIsWrittenAndTheFileEndsTerminated()
    {
        var result = Service().ConvertCp8d(
            Request(Row(name: "Ahmad Bin Ali"), Row(name: "Siti Binti Yusof")));

        var text = Unzip(result.Content!)["P1234567890_2026.TXT"];

        // LHDN's parsers reject a file whose last line has no terminator, so
        // the trailing CRLF leaves an empty final part.
        var lines = text.Split("\r\n");
        Assert.Equal(3, lines.Length);
        Assert.Equal(string.Empty, lines[2]);
        Assert.StartsWith("AHMAD BIN ALI|", lines[0]);
        Assert.StartsWith("SITI BINTI YUSOF|", lines[1]);
    }

    [Fact]
    public void AnEmployerNumberWithNoDigitsIsRefusedRatherThanRenderedEmpty()
    {
        var request = Request();
        request.EmployerNo = "E-/";

        var result = Service().ConvertCp8d(request);

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
    }
}
