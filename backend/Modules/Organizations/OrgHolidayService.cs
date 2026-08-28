using AltomateHR.Api.Modules.Organizations.Dtos;
using AltomateHR.Api.Modules.Organizations.Entities;

namespace AltomateHR.Api.Modules.Organizations;

public class OrgHolidayService : IOrgHolidayService
{
    private readonly IOrgHolidayRepository _repo;

    public OrgHolidayService(IOrgHolidayRepository repo) => _repo = repo;

    public async Task<IEnumerable<OrgHolidayDto>> GetAsync(int? year) =>
        (year is null ? await _repo.GetAllAsync() : await _repo.GetByYearAsync(year.Value))
            .Select(ToDto);

    public async Task<HolidaySaveResult> ReplaceYearAsync(int year, SaveHolidaysDto dto)
    {
        var rows = new List<OrgHoliday>();
        var seen = new HashSet<DateTime>();
        var now = DateTime.UtcNow;

        foreach (var h in dto.Holidays)
        {
            var date = h.Date!.Value.Date;
            if (date.Year != year)
                return new HolidaySaveResult(false, null,
                    $"{date:yyyy-MM-dd} is not in {year}.");

            // The unique index would reject this anyway; catching it here gives
            // the admin the offending date instead of a constraint violation.
            if (!seen.Add(date))
                return new HolidaySaveResult(false, null,
                    $"{date:yyyy-MM-dd} appears more than once.");

            rows.Add(new OrgHoliday { Date = date, Name = h.Name.Trim(), CreatedAt = now });
        }

        await _repo.ReplaceYearAsync(year, rows);
        return new HolidaySaveResult(true, rows.Select(ToDto), null);
    }

    public async Task<IReadOnlySet<DateTime>> GetDatesAsync(int year) =>
        (await _repo.GetByYearAsync(year)).Select(h => h.Date.Date).ToHashSet();

    private static OrgHolidayDto ToDto(OrgHoliday h) => new()
    {
        Id = h.Id,
        Date = h.Date,
        Name = h.Name,
    };
}
