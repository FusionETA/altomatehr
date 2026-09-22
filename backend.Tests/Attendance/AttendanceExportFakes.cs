using AltomateHR.Api.Common.Tabular;
using AltomateHR.Api.Modules.Holidays;
using AltomateHR.Api.Modules.Holidays.Dtos;
using AltomateHR.Api.Modules.Leave;
using AltomateHR.Api.Modules.Leave.Dtos;
using AltomateHR.Api.Modules.Leave.Entities;
using AltomateHR.Api.Modules.Realtime;

namespace AltomateHR.Api.Tests.Attendance;

// The two calendar sources the attendance export needs. Both default to
// "nothing", so a test only declares the days it actually cares about.
internal sealed class FakeHolidayService : IHolidayService
{
    private readonly List<HolidayDto> _rows;

    public FakeHolidayService(params (string Date, string Name)[] rows) =>
        _rows = [.. rows.Select(r => new HolidayDto { Id = r.Date, Date = r.Date, Name = r.Name })];

    public Task<IEnumerable<HolidayDto>> GetAllAsync() => Task.FromResult<IEnumerable<HolidayDto>>(_rows);

    public Task<IEnumerable<HolidayDto>> GetInRangeAsync(DateTime from, DateTime to) =>
        Task.FromResult<IEnumerable<HolidayDto>>(
            _rows.Where(h => DateTime.Parse(h.Date).Date >= from.Date
                          && DateTime.Parse(h.Date).Date <= to.Date));

    public Task<bool> IsHolidayAsync(DateTime date, string? projectId) =>
        Task.FromResult(_rows.Any(h => DateTime.Parse(h.Date).Date == date.Date));

    public Task<HolidaySaveResult> CreateAsync(SaveHolidayDto dto) => throw new NotSupportedException();
    public Task<HolidaySaveResult> UpdateAsync(string id, SaveHolidayDto dto) => throw new NotSupportedException();
    public Task<bool> DeleteAsync(string id) => throw new NotSupportedException();

    // Importing is an admin action; nothing under test here performs one.
    public Task<HolidayImportResult> ImportAsync(int year, string countryCode) =>
        throw new NotSupportedException();
}

internal sealed class FakeLeaveTypeService : ILeaveTypeService
{
    private readonly List<LeaveTypeDto> _types;

    public FakeLeaveTypeService(params (string Id, string Name)[] types) =>
        _types = [.. types.Select(t => new LeaveTypeDto { Id = t.Id, Name = t.Name })];

    public Task<IEnumerable<LeaveTypeDto>> GetAllAsync() =>
        Task.FromResult<IEnumerable<LeaveTypeDto>>(_types);

    public Task<LeaveTypeSaveResult> CreateAsync(SaveLeaveTypeDto dto) => throw new NotSupportedException();
    public Task<LeaveTypeSaveResult> UpdateAsync(string id, SaveLeaveTypeDto dto) => throw new NotSupportedException();
    public Task<LeaveTypeSaveResult> SetArchivedAsync(string id, bool archived) => throw new NotSupportedException();
    public Task<int> EnsureDefaultsAsync() => throw new NotSupportedException();
    public Task<int> EnsureDefaultsForOrganizationAsync(string organizationId) => throw new NotSupportedException();
    public Task<int> CountActiveTypesAsync() => throw new NotSupportedException();
}
