using System.Text;
using AltomateHR.Api.Common.Tabular;
using AltomateHR.Api.Modules.Claims;
using AltomateHR.Api.Modules.Claims.Dtos;
using AltomateHR.Api.Modules.Claims.Entities;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Tests.Support;

namespace AltomateHR.Api.Tests.Claims;

public class ClaimsImportExportTests
{
    private static readonly EmployeeIdentity Ahmad = new("usr-ahmad", "ahmad@x.com", "Ahmad Ali", "Employee");
    private static readonly EmployeeIdentity Siti = new("usr-siti", "siti@x.com", "Siti Nur", "Employee");

    private static ClaimsService Service(
        IEnumerable<Claim>? existing = null,
        params EmployeeIdentity[] members) =>
        ClaimsTestFactory.CreateService(
            existing ?? [],
            employees: new FakeEmployeeDirectory(members.Length > 0 ? members : [Ahmad, Siti]));

    private static byte[] Csv(params string[] lines) =>
        Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n");

    private const string Header = "*Employee Email,*Title,*Category,*Amount,Currency,*Spent On,Status";

    // ---- Import ----

    [Fact]
    public async Task ImportsAValidRow()
    {
        var repo = new FakeClaimsRepository([]);
        var service = ClaimsTestFactory.CreateService([], employees: new FakeEmployeeDirectory(Ahmad));

        var result = await service.ImportAsync(
            Csv(Header, "ahmad@x.com,Client lunch,MEAL,85.50,MYR,2026-01-15,APPROVED"),
            TabularFormat.Csv);

        Assert.Equal(1, result.Imported);
        Assert.Equal(0, result.Failed);
        Assert.Empty(result.Errors);

        var claim = Assert.Single(await service.GetMineAsync("usr-ahmad"));
        Assert.Equal("Client lunch", claim.Title);
        Assert.Equal(85.50m, claim.Amount);
        Assert.Equal(ClaimCategory.MEAL, claim.Category);
        Assert.Equal(ClaimStatus.APPROVED, claim.Status);
        Assert.Equal(new DateTime(2026, 1, 15), claim.SpentAt);
    }

    // A settled claim is the normal case for a migration; landing it PENDING
    // would drop somebody's whole history into an approver's queue.
    [Fact]
    public async Task DefaultsABlankStatusToApproved()
    {
        var service = Service(members: Ahmad);

        await service.ImportAsync(
            Csv(Header, "ahmad@x.com,Taxi,TRANSPORT,20,MYR,2026-02-01,"),
            TabularFormat.Csv);

        Assert.Equal(ClaimStatus.APPROVED, Assert.Single(await service.GetMineAsync("usr-ahmad")).Status);
    }

    [Fact]
    public async Task SkipsARowItAlreadyImported_SoAReRunIsSafe()
    {
        var service = Service(members: Ahmad);
        var file = Csv(Header, "ahmad@x.com,Client lunch,MEAL,85.50,MYR,2026-01-15,APPROVED");

        var first = await service.ImportAsync(file, TabularFormat.Csv);
        var second = await service.ImportAsync(file, TabularFormat.Csv);

        Assert.Equal(1, first.Imported);
        Assert.Equal(0, second.Imported);
        Assert.Equal(1, second.Skipped);
        Assert.Single(await service.GetMineAsync("usr-ahmad"));
    }

    [Fact]
    public async Task SkipsADuplicateWithinTheSameFile()
    {
        var service = Service(members: Ahmad);

        var result = await service.ImportAsync(
            Csv(Header,
                "ahmad@x.com,Client lunch,MEAL,85.50,MYR,2026-01-15,APPROVED",
                "ahmad@x.com,Client lunch,MEAL,85.50,MYR,2026-01-15,APPROVED"),
            TabularFormat.Csv);

        Assert.Equal(1, result.Imported);
        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public async Task ReportsBadRowsIndividuallyAndStillImportsTheGoodOnes()
    {
        var service = Service(members: Ahmad);

        var result = await service.ImportAsync(
            Csv(Header,
                "nobody@x.com,Lunch,MEAL,10,MYR,2026-01-15,APPROVED",     // row 2: unknown employee
                "ahmad@x.com,Lunch,NONSENSE,10,MYR,2026-01-15,APPROVED",  // row 3: bad category
                "ahmad@x.com,Lunch,MEAL,-5,MYR,2026-01-15,APPROVED",      // row 4: bad amount
                "ahmad@x.com,Lunch,MEAL,10,MYR,not-a-date,APPROVED",      // row 5: bad date
                "ahmad@x.com,Good one,MEAL,10,MYR,2026-01-15,APPROVED"),  // row 6: fine
            TabularFormat.Csv);

        Assert.Equal(1, result.Imported);
        Assert.Equal(4, result.Failed);
        Assert.Equal([2, 3, 4, 5], result.Errors.Select(e => e.Row));
    }

    // Names aren't unique, so guessing would file one person's claim against
    // another. The admin has to disambiguate with an email.
    [Fact]
    public async Task RefusesAnAmbiguousEmployeeName()
    {
        var service = Service(
            members: [new("usr-a", "a@x.com", "Ahmad Ali", "Employee"),
                      new("usr-b", "b@x.com", "Ahmad  ALI", "Employee")]);

        var result = await service.ImportAsync(
            Csv("Employee Name,*Title,*Category,*Amount,*Spent On",
                "Ahmad Ali,Lunch,MEAL,10,2026-01-15"),
            TabularFormat.Csv);

        Assert.Equal(0, result.Imported);
        Assert.Contains("Employee Email", Assert.Single(result.Errors).Message);
    }

    [Fact]
    public async Task ResolvesAnEmployeeByNameWhenTheNameIsUnique()
    {
        var service = Service(members: Ahmad);

        var result = await service.ImportAsync(
            Csv("Employee Name,*Title,*Category,*Amount,*Spent On",
                "ahmad  ali,Lunch,MEAL,10,2026-01-15"),
            TabularFormat.Csv);

        Assert.Equal(1, result.Imported);
    }

    [Fact]
    public async Task SkipsTheTemplatesUntouchedExampleRow()
    {
        var service = Service(members: Ahmad);
        var template = service.BuildImportTemplate(TabularFormat.Csv);

        var result = await service.ImportAsync(template.Content, TabularFormat.Csv);

        Assert.Equal(0, result.Imported);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Failed);
    }

    [Fact]
    public async Task NamesTheMissingRequiredColumns()
    {
        var service = Service(members: Ahmad);

        var result = await service.ImportAsync(
            Csv("Employee Email,Title", "ahmad@x.com,Lunch"),
            TabularFormat.Csv);

        var error = Assert.Single(result.Errors);
        Assert.Contains("Category", error.Message);
        Assert.Contains("Amount", error.Message);
        Assert.Contains("Spent On", error.Message);
    }

    [Fact]
    public async Task ReportsAHeaderOnlyFileAsAFileProblem()
    {
        var result = await Service(members: Ahmad).ImportAsync(Csv(Header), TabularFormat.Csv);

        Assert.Equal(0, result.Imported);
        Assert.Contains("no data rows", Assert.Single(result.Errors).Message);
    }

    [Fact]
    public async Task RoundTripsThroughXlsxToo()
    {
        var service = Service(members: Ahmad);
        var csv = Csv(Header, "ahmad@x.com,Client lunch,MEAL,85.50,MYR,2026-01-15,APPROVED");

        // Re-render the same rows as a workbook, then import that.
        var rows = TabularReader.Read(csv, TabularFormat.Csv);
        var sheet = new TabularSheet("Claims Import", rows[0]);
        foreach (var row in rows.Skip(1)) sheet.AddRow(row);

        var result = await service.ImportAsync(
            TabularWriter.Write(sheet, TabularFormat.Xlsx), TabularFormat.Xlsx);

        Assert.Equal(1, result.Imported);
    }

    // ---- Export ----

    [Fact]
    public async Task ExportsOneRowPerClaimWithTheEmployeeResolved()
    {
        var service = Service(
            [ClaimsTestFactory.NewClaim("c1", "usr-ahmad"), ClaimsTestFactory.NewClaim("c2", "usr-siti")]);

        var export = await service.ExportSummaryAsync(new ClaimsExportQueryDto(), TabularFormat.Csv);
        var rows = TabularReader.Read(export.Content, TabularFormat.Csv);

        Assert.Equal(3, rows.Count);                       // header + 2 claims
        Assert.Equal("Claim #", rows[0][0]);
        Assert.Contains("ahmad@x.com", rows.SelectMany(r => r));
        Assert.Contains("siti@x.com", rows.SelectMany(r => r));
        Assert.EndsWith(".csv", export.FileName);
    }

    [Fact]
    public async Task ExportHonoursTheDateAndStatusFilters()
    {
        var old = ClaimsTestFactory.NewClaim("c1", "usr-ahmad");
        old.SpentAt = new DateTime(2025, 1, 1);
        var recent = ClaimsTestFactory.NewClaim("c2", "usr-ahmad", ClaimStatus.APPROVED);
        recent.SpentAt = new DateTime(2026, 6, 1);

        var service = Service([old, recent]);

        var byDate = await service.ExportSummaryAsync(
            new ClaimsExportQueryDto { From = new DateTime(2026, 1, 1) }, TabularFormat.Csv);
        Assert.Equal(2, TabularReader.Read(byDate.Content, TabularFormat.Csv).Count);   // header + 1

        var byStatus = await service.ExportSummaryAsync(
            new ClaimsExportQueryDto { Status = ClaimStatus.APPROVED }, TabularFormat.Csv);
        Assert.Equal(2, TabularReader.Read(byStatus.Content, TabularFormat.Csv).Count);
    }

    [Fact]
    public async Task PdfExportRendersARealDocument()
    {
        var service = Service([
            ClaimsTestFactory.NewClaim("c1", "usr-ahmad"),
            ClaimsTestFactory.NewClaim("c2", "usr-siti"),
        ]);

        var export = await service.ExportSummaryAsync(new ClaimsExportQueryDto(), TabularFormat.Pdf);

        Assert.Equal("%PDF", Encoding.ASCII.GetString(export.Content.Take(4).ToArray()));
        Assert.EndsWith(".pdf", export.FileName);
        Assert.Equal("application/pdf", export.ContentType);
    }

    // An admin filtering down to nothing must get a document saying so, not a 500.
    [Fact]
    public async Task PdfExportRendersWhenNothingMatches()
    {
        var export = await Service([]).ExportSummaryAsync(
            new ClaimsExportQueryDto { From = new DateTime(2099, 1, 1) }, TabularFormat.Pdf);

        Assert.Equal("%PDF", Encoding.ASCII.GetString(export.Content.Take(4).ToArray()));
    }

    // The printable sheet is narrower than the spreadsheet on purpose — A4
    // landscape can't carry 23 legible columns.
    [Fact]
    public async Task PdfUsesTheNarrowerPrintableColumnSet()
    {
        var employees = EmployeeDirectoryTestFactory.Snapshot([Ahmad]);
        var claims = new[] { ClaimsTestFactory.NewClaim("c1", "usr-ahmad") };

        var spreadsheet = ClaimsSummarySheet.BuildExport(
            claims, employees, new Dictionary<string, string>(),
            new Dictionary<string, (string, string)>());

        var printable = ClaimsSummarySheet.BuildPrintable(
            claims, employees, new Dictionary<string, string>(),
            new Dictionary<string, (string, string)>(), "Jan 2026");

        Assert.True(printable.Headers.Count < spreadsheet.Headers.Count);
        Assert.True(printable.Headers.Count <= TabularPdfRenderer.ComfortableColumnCount);
        Assert.NotNull(printable.TotalsRow);
        Assert.Equal("Jan 2026", printable.Caption);
    }

    // Summing MYR and USD into one number would be worse than useless.
    [Fact]
    public void PdfTotalsAreSplitPerCurrency()
    {
        var myr = ClaimsTestFactory.NewClaim("c1", "usr-ahmad");
        myr.Currency = "MYR";
        myr.Amount = 100m;
        var usd = ClaimsTestFactory.NewClaim("c2", "usr-ahmad");
        usd.Currency = "USD";
        usd.Amount = 50m;

        var printable = ClaimsSummarySheet.BuildPrintable(
            [myr, usd], EmployeeDirectoryTestFactory.Snapshot([Ahmad]),
            new Dictionary<string, string>(), new Dictionary<string, (string, string)>(), "");

        var totals = string.Join("|", printable.TotalsRow!);
        Assert.Contains("MYR 100.00", totals);
        Assert.Contains("USD 50.00", totals);
        Assert.Contains("2 claim(s)", totals);
    }

    [Fact]
    public async Task XlsxExportCarriesTheXlsxNameAndContentType()
    {
        var service = Service([ClaimsTestFactory.NewClaim("c1", "usr-ahmad")]);

        var export = await service.ExportSummaryAsync(new ClaimsExportQueryDto(), TabularFormat.Xlsx);

        Assert.EndsWith(".xlsx", export.FileName);
        Assert.Equal(TabularFormats.XlsxContentType, export.ContentType);
        // Readable as a workbook, not just bytes with the right name.
        Assert.NotEmpty(TabularReader.Read(export.Content, TabularFormat.Xlsx));
    }
}
