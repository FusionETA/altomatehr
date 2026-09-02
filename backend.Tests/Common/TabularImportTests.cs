using AltomateHR.Api.Common.Tabular;

namespace AltomateHR.Api.Tests.Common;

// Header matching, cell parsing and the template's example row — the three
// places a real admin's file diverges from the one we handed them.
public class TabularImportTests
{
    private static readonly IReadOnlyList<TabularColumn> Columns =
    [
        new("employeeEmail", "Employee Email", true, "ahmad@company.com", ["email", "member"]),
        new("days", "Days", true, "2"),
        new("reason", "Reason", false, "Family matter"),
    ];

    [Fact]
    public void MatchesHeadersIgnoringCasePunctuationAndTheRequiredMarker()
    {
        var (map, missing) = TabularHeaderMap.Build(["*EMPLOYEE_EMAIL", " days ", "Reason"], Columns);

        Assert.Empty(missing);
        Assert.NotNull(map);
        Assert.Equal("a@b.com", map!.Cell(["a@b.com", "3", "x"], "employeeEmail"));
        Assert.Equal("3", map.Cell(["a@b.com", "3", "x"], "days"));
    }

    [Fact]
    public void MatchesAliases_SoAnotherSystemsExportWorksUnedited()
    {
        var (map, missing) = TabularHeaderMap.Build(["Member", "Days"], Columns);

        Assert.Empty(missing);
        Assert.Equal("ali@x.com", map!.Cell(["ali@x.com", "1"], "employeeEmail"));
    }

    [Fact]
    public void NamesEveryMissingRequiredColumnAtOnce()
    {
        var (map, missing) = TabularHeaderMap.Build(["Reason"], Columns);

        Assert.Null(map);
        Assert.Equal(["Employee Email", "Days"], missing);
    }

    [Fact]
    public void ReturnsEmptyForAnAbsentColumnOrAShortRow()
    {
        var (map, _) = TabularHeaderMap.Build(["Employee Email", "Days"], Columns);

        Assert.Equal("", map!.Cell(["a@b.com", "2"], "reason"));   // column not in the file
        Assert.Equal("", map.Cell(["a@b.com"], "days"));            // row ends early
    }

    [Fact]
    public void RecognisesTheUntouchedTemplateExampleRow_ByKeyNotPosition()
    {
        // Columns reordered, as an admin rearranging the template would leave it.
        var (map, _) = TabularHeaderMap.Build(["Days", "Employee Email", "Reason"], Columns);

        Assert.True(TabularTemplate.IsExampleRow(
            map!, ["2", "ahmad@company.com", "Family matter"], Columns));

        Assert.False(TabularTemplate.IsExampleRow(
            map!, ["2", "siti@company.com", "Family matter"], Columns));
    }

    [Theory]
    [InlineData("2026-01-15", 2026, 1, 15)]
    [InlineData("2026/01/15", 2026, 1, 15)]
    [InlineData("15/01/2026", 2026, 1, 15)]   // day-first: the ambiguous case, pinned
    public void ParsesTheDateFormatsSpreadsheetsActuallyProduce(string input, int y, int m, int d)
    {
        Assert.Equal(new DateTime(y, m, d), TabularCell.Date(input));
    }

    [Fact]
    public void RejectsANonDateRatherThanGuessing()
    {
        Assert.Null(TabularCell.Date("sometime in January"));
        Assert.Null(TabularCell.Date(""));
    }

    [Fact]
    public void ResolvesABareTimeAgainstTheRowsDate()
    {
        var instant = TabularCell.Instant("09:03", new DateTime(2026, 1, 15));

        Assert.Equal(new DateTime(2026, 1, 15, 9, 3, 0, DateTimeKind.Utc), instant);
    }

    [Fact]
    public void StripsCurrencySymbolsAndThousandsSeparatorsFromMoney()
    {
        Assert.Equal(1250.50m, TabularCell.Money("RM 1,250.50"));
        Assert.Null(TabularCell.Money("free"));
    }

    [Fact]
    public void MatchesEnumsIgnoringCaseAndPunctuation()
    {
        Assert.Equal(SampleStatus.CLOCKED_OUT, TabularCell.Enum<SampleStatus>("clocked out"));
        Assert.Equal(SampleStatus.CLOCKED_OUT, TabularCell.Enum<SampleStatus>("ClockedOut"));
        Assert.Null(TabularCell.Enum<SampleStatus>("napping"));
    }

    [Fact]
    public void TruncatesTextToAColumnsLength_RatherThanFailingTheWholeBatch()
    {
        // MySQL would reject the row outright; losing the tail of a free-text
        // note is the lesser harm during a migration.
        Assert.Equal("abcde", TabularCell.Text("abcdefghij", 5));
        Assert.Equal("short", TabularCell.Text("  short  ", 20));
        Assert.Null(TabularCell.Text("   ", 20));
    }

    [Fact]
    public void CapsTheErrorListSoAWhollyWrongFileDoesNotReturnThousands()
    {
        var result = new TabularImportResult();
        for (var i = 0; i < 500; i++) result.Fail(i + 2, "nope");

        Assert.Equal(500, result.Failed);         // still counted honestly
        Assert.Equal(200, result.Errors.Count);   // but not all listed
    }

    private enum SampleStatus { CLOCKED_IN, CLOCKED_OUT }
}
