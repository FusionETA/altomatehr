using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace AltomateHR.Api.Modules.Holidays;

// One country-year of public holidays, fetched from a public calendar API.
//
// Two upstreams, because neither covers everyone:
//
//   Calendarific  — needs a key, but has Malaysia. Preferred when configured.
//   date.nager.at — free and needs nothing, but has NO DATA AT ALL for MY,
//                   which is this app's main market. Kept as the fallback so
//                   an org elsewhere can still import without a key.
//
// A failure here is an admin-facing message, not an exception: "Calendarific
// returned 403" is something they can act on, and a 500 is not.
public interface IPublicHolidaySource
{
    Task<HolidayFetchResult> FetchAsync(int year, string countryCode, CancellationToken ct = default);
}

public readonly record struct FetchedHoliday(DateTime Date, string Name);

// Source is null when nothing was fetched.
public readonly record struct HolidayFetchResult(
    bool Ok, IReadOnlyList<FetchedHoliday> Holidays, string? Source, string? Error)
{
    public static HolidayFetchResult Failed(string error) => new(false, [], null, error);

    public static HolidayFetchResult Success(IReadOnlyList<FetchedHoliday> holidays, string source) =>
        new(true, holidays, source, null);
}

public class PublicHolidaySource : IPublicHolidaySource
{
    public const string Nager = "date.nager.at";
    public const string Calendarific = "Calendarific";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly HolidayImportOptions _options;

    public PublicHolidaySource(HttpClient http, IOptions<HolidayImportOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task<HolidayFetchResult> FetchAsync(
        int year, string countryCode, CancellationToken ct = default)
    {
        var key = _options.CalendarificApiKey?.Trim() ?? string.Empty;

        // No key: nager is the only option, so its failure is the whole story.
        if (key.Length == 0) return await FetchNagerAsync(year, countryCode, ct);

        var primary = await FetchCalendarificAsync(year, countryCode, key, ct);
        if (primary.Ok) return primary;

        var fallback = await FetchNagerAsync(year, countryCode, ct);
        if (fallback.Ok) return fallback;

        // Both legs failed. Lead with the preferred source's reason and keep
        // the other, so the admin can tell which one actually broke.
        return HolidayFetchResult.Failed($"{primary.Error} ({fallback.Error})");
    }

    private async Task<HolidayFetchResult> FetchNagerAsync(
        int year, string countryCode, CancellationToken ct)
    {
        try
        {
            var url = $"https://date.nager.at/api/v3/PublicHolidays/{year}/{Uri.EscapeDataString(countryCode)}";
            using var response = await _http.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
                return HolidayFetchResult.Failed($"date.nager.at returned {(int)response.StatusCode}.");

            // nager answers 204 — or, rarely, 200 with an empty body — for a
            // country it has no calendar for. Malaysia is one of them. That is
            // a SUCCESS status, so without this guard the empty body would be
            // parsed and reported as "could not reach date.nager.at", which
            // sends the admin looking at their network instead of their key.
            var body = (await response.Content.ReadAsStringAsync(ct)).Trim();
            if (body.Length == 0)
            {
                return HolidayFetchResult.Failed(
                    $"date.nager.at has no public-holiday data for {countryCode}. "
                    + "Set a Calendarific API key to import this country.");
            }

            var raw = JsonSerializer.Deserialize<List<NagerHoliday>>(body, JsonOptions) ?? [];
            var holidays = raw
                .Select(h => (Parsed: ParseDate(h.Date), h.LocalName, h.Name))
                .Where(x => x.Parsed is not null)
                .Select(x => new FetchedHoliday(
                    x.Parsed!.Value, Clean(x.LocalName) ?? Clean(x.Name) ?? "Public holiday"))
                .ToList();

            return HolidayFetchResult.Success(holidays, Nager);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return HolidayFetchResult.Failed("Could not reach date.nager.at.");
        }
    }

    private async Task<HolidayFetchResult> FetchCalendarificAsync(
        int year, string countryCode, string key, CancellationToken ct)
    {
        try
        {
            var url = "https://calendarific.com/api/v2/holidays"
                + $"?api_key={Uri.EscapeDataString(key)}"
                + $"&country={Uri.EscapeDataString(countryCode)}"
                + $"&year={year}&type=national";

            using var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
                return HolidayFetchResult.Failed($"Calendarific returned {(int)response.StatusCode}.");

            var payload = JsonSerializer.Deserialize<CalendarificResponse>(
                await response.Content.ReadAsStringAsync(ct), JsonOptions);

            // Calendarific answers 200 with the real code in the body, so an
            // expired key arrives as a success with meta.code 401.
            if (payload?.Meta is { Code: not null and not 200 } meta)
            {
                return HolidayFetchResult.Failed(
                    Clean(meta.ErrorDetail) ?? $"Calendarific error {meta.Code}.");
            }

            var holidays = (payload?.Response?.Holidays ?? [])
                .Select(h => (Parsed: ParseDate(TenChars(h.Date?.Iso)), h.Name))
                .Where(x => x.Parsed is not null)
                .Select(x => new FetchedHoliday(x.Parsed!.Value, Clean(x.Name) ?? "Public holiday"))
                .ToList();

            return HolidayFetchResult.Success(holidays, Calendarific);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return HolidayFetchResult.Failed("Could not reach Calendarific.");
        }
    }

    // Calendarific's iso is sometimes a full timestamp with an offset; only the
    // calendar date matters, and the offset is the SOURCE's timezone, not ours
    // — parsing the whole thing would shift a holiday by a day.
    private static string? TenChars(string? value) =>
        value is { Length: >= 10 } ? value[..10] : null;

    private static DateTime? ParseDate(string? value) =>
        DateTime.TryParseExact(
            value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var parsed)
            ? DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc)
            : null;

    private static string? Clean(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private sealed class NagerHoliday
    {
        public string? Date { get; set; }
        public string? LocalName { get; set; }
        public string? Name { get; set; }
    }

    private sealed class CalendarificResponse
    {
        public CalendarificMeta? Meta { get; set; }
        public CalendarificPayload? Response { get; set; }
    }

    private sealed class CalendarificMeta
    {
        public int? Code { get; set; }

        // Snake_case on the wire. PropertyNameCaseInsensitive bridges casing,
        // not the underscore, so without this the reason an admin needs —
        // "Invalid API key" — silently binds to null and they get the bare
        // numeric code instead.
        [JsonPropertyName("error_detail")]
        public string? ErrorDetail { get; set; }
    }

    private sealed class CalendarificPayload
    {
        public List<CalendarificHoliday>? Holidays { get; set; }
    }

    private sealed class CalendarificHoliday
    {
        public string? Name { get; set; }
        public CalendarificDate? Date { get; set; }
    }

    private sealed class CalendarificDate
    {
        public string? Iso { get; set; }
    }
}
