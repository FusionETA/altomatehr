using System.Net;
using System.Text;
using AltomateHR.Api.Modules.Holidays;
using Microsoft.Extensions.Options;

namespace AltomateHR.Api.Tests.Holidays;

// Reading the two upstream calendars.
//
// The parsing is where this breaks quietly: both APIs answer 200 for things
// that are not success, and a misread body becomes either a missing holiday or
// a holiday on the wrong day — both of which land in someone's leave balance.
public class PublicHolidaySourceTests
{
    private static PublicHolidaySource Make(string? key, params (string UrlPart, HttpResponseMessage Response)[] routes) =>
        new(new HttpClient(new RouteHandler(routes)),
            Options.Create(new HolidayImportOptions { CalendarificApiKey = key ?? string.Empty }));

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Empty(HttpStatusCode code) =>
        new(code) { Content = new StringContent("", Encoding.UTF8, "application/json") };

    // ─── date.nager.at ──────────────────────────────────────────────────

    [Fact]
    public async Task NagerPrefersTheLocalNameOverTheEnglishOne()
    {
        var source = Make(null, ("nager", Json(
            """[{"date":"2026-01-01","localName":"Tahun Baharu","name":"New Year's Day"}]""")));

        var result = await source.FetchAsync(2026, "MY");

        Assert.True(result.Ok, result.Error);
        var one = Assert.Single(result.Holidays);
        Assert.Equal("Tahun Baharu", one.Name);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), one.Date);
    }

    // nager answers 204 — a SUCCESS status — for a country it has no calendar
    // for, and Malaysia is one of them. Without the guard the empty body parses
    // as a failure and reports "could not reach date.nager.at", which sends the
    // admin to look at their network instead of at their missing API key.
    [Theory]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.OK)]
    public async Task NagerReportsAnEmptyBodyAsMissingCoverage(HttpStatusCode code)
    {
        var source = Make(null, ("nager", Empty(code)));

        var result = await source.FetchAsync(2026, "MY");

        Assert.False(result.Ok);
        Assert.Contains("no public-holiday data for MY", result.Error!);
        Assert.Contains("Calendarific", result.Error!);
    }

    // ─── Calendarific ───────────────────────────────────────────────────

    [Fact]
    public async Task CalendarificIsPreferredWhenAKeyIsConfigured()
    {
        var source = Make("key-1",
            ("calendarific", Json(
                """{"meta":{"code":200},"response":{"holidays":[{"name":"Hari Raya","date":{"iso":"2026-03-21"}}]}}""")),
            ("nager", Json("""[{"date":"2026-01-01","name":"Should not be used"}]""")));

        var result = await source.FetchAsync(2026, "MY");

        Assert.True(result.Ok, result.Error);
        Assert.Equal(PublicHolidaySource.Calendarific, result.Source);
        Assert.Equal("Hari Raya", Assert.Single(result.Holidays).Name);
    }

    // Calendarific answers HTTP 200 and puts the real code in the body, so an
    // expired key arrives looking like a success.
    [Fact]
    public async Task CalendarificReportsAnErrorCarriedInABodyWithStatus200()
    {
        var source = Make("expired",
            ("calendarific", Json("""{"meta":{"code":401,"error_detail":"Invalid API key"}}""")),
            ("nager", Empty(HttpStatusCode.NoContent)));

        var result = await source.FetchAsync(2026, "MY");

        Assert.False(result.Ok);
        Assert.Contains("Invalid API key", result.Error!);
    }

    // The iso field sometimes carries a full timestamp with the SOURCE's
    // offset. Parsing the whole thing would shift the holiday into the
    // neighbouring day, which is a day of someone's leave.
    [Fact]
    public async Task CalendarificKeepsTheCalendarDateFromATimestampWithAnOffset()
    {
        var source = Make("key-1", ("calendarific", Json(
            """{"meta":{"code":200},"response":{"holidays":[{"name":"Wesak","date":{"iso":"2026-05-01T00:00:00+08:00"}}]}}""")));

        var result = await source.FetchAsync(2026, "MY");

        Assert.Equal(
            new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            Assert.Single(result.Holidays).Date);
    }

    // ─── Falling between the two ────────────────────────────────────────

    [Fact]
    public async Task FallsBackToNagerWhenCalendarificFails()
    {
        var source = Make("key-1",
            ("calendarific", Empty(HttpStatusCode.InternalServerError)),
            ("nager", Json("""[{"date":"2026-01-01","localName":"New Year"}]""")));

        var result = await source.FetchAsync(2026, "SG");

        Assert.True(result.Ok, result.Error);
        Assert.Equal(PublicHolidaySource.Nager, result.Source);
    }

    // When both legs fail the admin needs to know WHICH — the key or the
    // network — so neither reason is thrown away.
    [Fact]
    public async Task KeepsBothReasonsWhenNeitherUpstreamAnswers()
    {
        var source = Make("key-1",
            ("calendarific", Empty(HttpStatusCode.Forbidden)),
            ("nager", Empty(HttpStatusCode.NoContent)));

        var result = await source.FetchAsync(2026, "MY");

        Assert.False(result.Ok);
        Assert.Contains("Calendarific returned 403", result.Error!);
        Assert.Contains("no public-holiday data", result.Error!);
    }

    // A dead upstream is an admin-facing message, not a 500.
    [Fact]
    public async Task AnUnreachableHostIsReportedRatherThanThrown()
    {
        var source = new PublicHolidaySource(
            new HttpClient(new ThrowingHandler()),
            Options.Create(new HolidayImportOptions()));

        var result = await source.FetchAsync(2026, "MY");

        Assert.False(result.Ok);
        Assert.Contains("Could not reach", result.Error!);
    }

    private sealed class RouteHandler((string UrlPart, HttpResponseMessage Response)[] routes)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            foreach (var (part, response) in routes)
            {
                if (url.Contains(part, StringComparison.OrdinalIgnoreCase))
                    return Task.FromResult(response);
            }

            throw new InvalidOperationException($"No stub for {url}");
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("dns");
    }
}
