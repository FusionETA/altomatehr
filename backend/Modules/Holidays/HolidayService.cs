using AltomateHR.Api.Modules.Holidays.Dtos;
using AltomateHR.Api.Modules.Holidays.Entities;
using AltomateHR.Api.Modules.Projects;

namespace AltomateHR.Api.Modules.Holidays;

// Public-holiday CRUD + the "is this date a holiday?" lookup the OT rate
// resolver depends on. Dates are normalised to UTC midnight so a date-only
// value compares cleanly regardless of what time component the client sent.
public class HolidayService : IHolidayService
{
    private readonly IHolidayRepository _holidays;
    private readonly IProjectService _projects;
    private readonly IPublicHolidaySource _source;

    public HolidayService(
        IHolidayRepository holidays, IProjectService projects, IPublicHolidaySource source)
    {
        _holidays = holidays;
        _projects = projects;
        _source = source;
    }

    public async Task<IEnumerable<HolidayDto>> GetAllAsync() =>
        (await _holidays.GetAllAsync()).Select(ToDto);

    public async Task<IEnumerable<HolidayDto>> GetInRangeAsync(DateTime from, DateTime to) =>
        (await _holidays.GetInRangeAsync(DateOnlyUtc(from), DateOnlyUtc(to))).Select(ToDto);

    public async Task<HolidaySaveResult> CreateAsync(SaveHolidayDto dto)
    {
        var (ok, error, projectId, date, name) = await ValidateAsync(dto);
        if (!ok) return new HolidaySaveResult(false, null, error);

        if (await _holidays.GetByDateAndScopeAsync(date, projectId) is not null)
            return new HolidaySaveResult(false, null,
                projectId is null
                    ? $"An org-wide holiday already exists on {date:yyyy-MM-dd}."
                    : $"This project already has a holiday on {date:yyyy-MM-dd}.");

        var now = DateTime.UtcNow;
        var saved = await _holidays.AddAsync(new Holiday
        {
            ProjectId = projectId,
            Date = date,
            Name = name,
            CreatedAt = now,
            UpdatedAt = now,
        });
        return new HolidaySaveResult(true, ToDto(saved));
    }

    public async Task<HolidaySaveResult> UpdateAsync(string id, SaveHolidayDto dto)
    {
        var holiday = await _holidays.GetByIdAsync(id);
        if (holiday is null) return new HolidaySaveResult(false, null, null);   // → 404

        var (ok, error, projectId, date, name) = await ValidateAsync(dto);
        if (!ok) return new HolidaySaveResult(false, null, error);

        var clash = await _holidays.GetByDateAndScopeAsync(date, projectId);
        if (clash is not null && clash.Id != id)
            return new HolidaySaveResult(false, null,
                $"A holiday already exists on {date:yyyy-MM-dd} for this scope.");

        holiday.ProjectId = projectId;
        holiday.Date = date;
        holiday.Name = name;
        holiday.UpdatedAt = DateTime.UtcNow;
        await _holidays.UpdateAsync(holiday);
        return new HolidaySaveResult(true, ToDto(holiday));
    }

    public async Task<bool> DeleteAsync(string id)
    {
        var holiday = await _holidays.GetByIdAsync(id);
        if (holiday is null) return false;
        await _holidays.DeleteAsync(holiday);
        return true;
    }

    public async Task<bool> IsHolidayAsync(DateTime date, string? projectId) =>
        (await _holidays.GetForDateAsync(DateOnlyUtc(date), projectId)).Count > 0;

    public async Task<HolidayImportResult> ImportAsync(int year, string countryCode)
    {
        // Bounded because the year reaches a third-party URL, and because no
        // payroll question is asked about the year 9999.
        if (year is < 2000 or > 2100)
            return new HolidayImportResult(false, 0, 0, null, "Year must be between 2000 and 2100.");

        var country = (countryCode ?? string.Empty).Trim().ToUpperInvariant();
        if (country.Length != 2 || !country.All(char.IsAsciiLetterUpper))
        {
            return new HolidayImportResult(false, 0, 0, null,
                "Country code must be two letters, like MY.");
        }

        var fetched = await _source.FetchAsync(year, country);
        if (!fetched.Ok) return new HolidayImportResult(false, 0, 0, null, fetched.Error);

        // One row per DATE. Both upstreams list a day twice when two holidays
        // fall on it — Malaysia has several — and the table is unique on
        // (scope, date), so the second insert would fail. First name wins.
        var byDate = new Dictionary<DateTime, string>();
        foreach (var holiday in fetched.Holidays)
        {
            byDate.TryAdd(DateOnlyUtc(holiday.Date), holiday.Name);
        }

        if (byDate.Count == 0)
        {
            return new HolidayImportResult(false, 0, 0, fetched.Source,
                $"No holidays came back for {country} {year}.");
        }

        // One read for the whole year rather than a lookup per date.
        var existing = (await _holidays.GetInRangeAsync(
                new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(year, 12, 31, 0, 0, 0, DateTimeKind.Utc)))
            .Where(h => h.ProjectId is null)
            .Select(h => h.Date.Date)
            .ToHashSet();

        var now = DateTime.UtcNow;
        var imported = 0;
        var skipped = 0;

        foreach (var (date, name) in byDate.OrderBy(pair => pair.Key))
        {
            if (!existing.Add(date.Date)) { skipped++; continue; }

            await _holidays.AddAsync(new Holiday
            {
                ProjectId = null,
                Date = date,
                Name = Truncate(name, 160),
                CreatedAt = now,
                UpdatedAt = now,
            });
            imported++;
        }

        return new HolidayImportResult(true, imported, skipped, fetched.Source);
    }

    // The column is 160; an upstream name longer than that would throw on save
    // rather than import.
    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private async Task<(bool Ok, string? Error, string? ProjectId, DateTime Date, string Name)> ValidateAsync(
        SaveHolidayDto dto)
    {
        var name = dto.Name.Trim();
        if (name.Length == 0)
            return (false, "Holiday name is required.", null, default, string.Empty);

        var projectId = string.IsNullOrWhiteSpace(dto.ProjectId) ? null : dto.ProjectId;
        if (projectId is not null && await _projects.GetByIdAsync(projectId) is null)
            return (false, "Project not found.", null, default, string.Empty);

        return (true, null, projectId, DateOnlyUtc(dto.Date), name);
    }

    // Strip the time component and stamp UTC, matching how AttendanceRecord.Date
    // stores a pure per-day bucket.
    private static DateTime DateOnlyUtc(DateTime d) =>
        DateTime.SpecifyKind(d.Date, DateTimeKind.Utc);

    private static HolidayDto ToDto(Holiday h) => new()
    {
        Id = h.Id,
        ProjectId = h.ProjectId,
        Date = h.Date.ToString("yyyy-MM-dd"),
        Name = h.Name,
    };
}
