using AltomateHR.Api.Modules.Holidays;
using AltomateHR.Api.Modules.Holidays.Entities;
using AltomateHR.Api.Tests.Support;

namespace AltomateHR.Api.Tests.Holidays;

// Importing a country's public-holiday calendar.
//
// This is what makes the leave rule real. LeaveAccrualMath already skips a
// holiday when counting days (see LeaveDayCountingTests), but no org had a
// single holiday row, so the branch never fired and a day off over Hari Raya
// was billed to the employee's balance.
public class HolidayImportTests
{
    private static (HolidayService Service, List<Holiday> Rows) Make(
        HolidayFetchResult fetch, params Holiday[] existing)
    {
        var rows = existing.ToList();
        return (
            new HolidayService(
                new StubHolidayRepository(rows),
                new FakeProjectServiceForExport(),
                new StubSource(fetch)),
            rows);
    }

    private static FetchedHoliday On(int y, int m, int d, string name) =>
        new(new DateTime(y, m, d, 0, 0, 0, DateTimeKind.Utc), name);

    private static HolidayFetchResult Fetched(params FetchedHoliday[] holidays) =>
        HolidayFetchResult.Success(holidays, PublicHolidaySource.Calendarific);

    [Fact]
    public async Task ImportsEachDayAsAnOrgWideHoliday()
    {
        var (service, rows) = Make(Fetched(
            On(2026, 1, 1, "New Year's Day"),
            On(2026, 5, 1, "Labour Day")));

        var result = await service.ImportAsync(2026, "MY");

        Assert.True(result.Ok, result.Error);
        Assert.Equal(2, result.Imported);
        Assert.Equal(2, rows.Count);
        // Org-wide, not pinned to a project: a project row is an ADDITION for a
        // site observing an extra state holiday, so importing into one would
        // leave everybody else working through the national calendar.
        Assert.All(rows, r => Assert.Null(r.ProjectId));
        Assert.All(rows, r => Assert.Equal(DateTimeKind.Utc, r.Date.Kind));
    }

    // Malaysia genuinely has days carrying two holidays. The table is unique on
    // (scope, date), so a second insert would throw mid-import and leave the
    // year half-loaded.
    [Fact]
    public async Task CollapsesTwoHolidaysThatFallOnTheSameDay()
    {
        var (service, rows) = Make(Fetched(
            On(2026, 5, 1, "Labour Day"),
            On(2026, 5, 1, "Wesak Day")));

        var result = await service.ImportAsync(2026, "MY");

        Assert.True(result.Ok, result.Error);
        Assert.Equal(1, result.Imported);
        Assert.Equal("Labour Day", Assert.Single(rows).Name);
    }

    // Re-running after a calendar revision is normal. An existing date is left
    // alone rather than overwritten — an admin may have renamed it or moved the
    // observed day, and silently undoing that is worse than importing nothing.
    [Fact]
    public async Task LeavesADateTheOrgAlreadyHasUntouched()
    {
        var mine = new Holiday
        {
            Date = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Name = "New Year (office closed Fri too)",
        };
        var (service, rows) = Make(
            Fetched(On(2026, 1, 1, "New Year's Day"), On(2026, 5, 1, "Labour Day")),
            mine);

        var result = await service.ImportAsync(2026, "MY");

        Assert.True(result.Ok, result.Error);
        Assert.Equal(1, result.Imported);
        Assert.Equal(1, result.Skipped);
        Assert.Equal("New Year (office closed Fri too)", rows[0].Name);
    }

    // A project-specific row for the same date does NOT block the org-wide one:
    // the two coexist by design, and treating the project row as "already have
    // it" would leave the rest of the org without the national holiday.
    [Fact]
    public async Task AProjectRowOnTheSameDateDoesNotCountAsAlreadyHavingIt()
    {
        var (service, rows) = Make(
            Fetched(On(2026, 1, 1, "New Year's Day")),
            new Holiday
            {
                ProjectId = "proj-1",
                Date = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Name = "Site shutdown",
            });

        var result = await service.ImportAsync(2026, "MY");

        Assert.Equal(1, result.Imported);
        Assert.Equal(0, result.Skipped);
        Assert.Contains(rows, r => r.ProjectId is null);
    }

    [Theory]
    [InlineData(1999, "MY")]
    [InlineData(2101, "MY")]
    [InlineData(2026, "MYS")]
    [InlineData(2026, "")]
    [InlineData(2026, "M1")]
    public async Task RefusesAYearOrCountryItCannotAskFor(int year, string country)
    {
        var (service, rows) = Make(Fetched(On(2026, 1, 1, "New Year's Day")));

        var result = await service.ImportAsync(year, country);

        Assert.False(result.Ok);
        Assert.Empty(rows);
    }

    [Fact]
    public async Task AcceptsALowercaseCountryCode()
    {
        var (service, _) = Make(Fetched(On(2026, 1, 1, "New Year's Day")));

        Assert.True((await service.ImportAsync(2026, "my")).Ok);
    }

    // The upstream's message is what the admin can act on — "Calendarific
    // returned 403" points at the key, and a generic failure points nowhere.
    [Fact]
    public async Task SurfacesTheUpstreamFailureUnchanged()
    {
        var (service, rows) = Make(HolidayFetchResult.Failed("Calendarific returned 403."));

        var result = await service.ImportAsync(2026, "MY");

        Assert.False(result.Ok);
        Assert.Equal("Calendarific returned 403.", result.Error);
        Assert.Empty(rows);
    }

    [Fact]
    public async Task RefusesWhenTheYearCameBackEmpty()
    {
        var (service, _) = Make(HolidayFetchResult.Success([], PublicHolidaySource.Nager));

        var result = await service.ImportAsync(2026, "MY");

        Assert.False(result.Ok);
        Assert.Contains("MY 2026", result.Error!);
    }

    // The name column is 160 characters; a longer one would throw on save and
    // abandon the rest of the year.
    [Fact]
    public async Task TrimsAnOverlongHolidayNameRatherThanFailingTheImport()
    {
        var (service, rows) = Make(Fetched(On(2026, 1, 1, new string('x', 400))));

        Assert.True((await service.ImportAsync(2026, "MY")).Ok);
        Assert.Equal(160, Assert.Single(rows).Name.Length);
    }

    private sealed class StubSource(HolidayFetchResult result) : IPublicHolidaySource
    {
        public Task<HolidayFetchResult> FetchAsync(
            int year, string countryCode, CancellationToken ct = default) =>
            Task.FromResult(result);
    }

    private sealed class StubHolidayRepository(List<Holiday> rows) : IHolidayRepository
    {
        public Task<List<Holiday>> GetAllAsync() => Task.FromResult(rows);

        public Task<List<Holiday>> GetInRangeAsync(DateTime from, DateTime to) =>
            Task.FromResult(rows.Where(r => r.Date >= from && r.Date <= to).ToList());

        public Task<Holiday?> GetByIdAsync(string id) =>
            Task.FromResult(rows.FirstOrDefault(r => r.Id == id));

        public Task<Holiday?> GetByDateAndScopeAsync(DateTime date, string? projectId) =>
            Task.FromResult(rows.FirstOrDefault(r => r.Date == date && r.ProjectId == projectId));

        public Task<List<Holiday>> GetForDateAsync(DateTime date, string? projectId) =>
            Task.FromResult(rows
                .Where(r => r.Date == date && (r.ProjectId is null || r.ProjectId == projectId))
                .ToList());

        public Task<Holiday> AddAsync(Holiday holiday) { rows.Add(holiday); return Task.FromResult(holiday); }
        public Task UpdateAsync(Holiday holiday) => Task.CompletedTask;
        public Task DeleteAsync(Holiday holiday) { rows.Remove(holiday); return Task.CompletedTask; }
    }
}
