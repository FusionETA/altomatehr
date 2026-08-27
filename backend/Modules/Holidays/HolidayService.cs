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

    public HolidayService(IHolidayRepository holidays, IProjectService projects)
    {
        _holidays = holidays;
        _projects = projects;
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
