using AltomateHR.Api.Modules.Overtime.Entities;

namespace AltomateHR.Api.Modules.Overtime;

public class ApprovedOvertimeService : IApprovedOvertimeService
{
    private readonly IOvertimeRepository _requests;
    private readonly IOtRateService _otRates;

    public ApprovedOvertimeService(IOvertimeRepository requests, IOtRateService otRates)
    {
        _requests = requests;
        _otRates = otRates;
    }

    public async Task<IReadOnlyDictionary<string, ApprovedOvertimeMinutes>> GetApprovedMinutesAsync(
        DateTime from, DateTime to, string? employeeId = null)
    {
        var start = from.Date;
        var end = to.Date;
        var result = new Dictionary<string, ApprovedOvertimeMinutes>(StringComparer.Ordinal);
        if (end < start) return result;

        var approved = (await _requests.GetAllAsync())
            .Where(r => r.Status == OvertimeStatus.APPROVED
                        && r.RequestedMinutes > 0
                        && (employeeId is null || r.EmployeeId == employeeId)
                        && r.WorkDate.Date >= start
                        && r.WorkDate.Date <= end)
            .ToList();

        // Classified through the SAME code the OT rate and the attendance hours
        // summary use, so a Saturday cannot be a rest day in one place and a
        // normal day in another. Once per employee + project: the holiday
        // calendar can be project-specific (a Penang site keeps a different set
        // from a Selangor HQ), and the batched call resolves the shift and the
        // calendar once for the whole range.
        foreach (var group in approved.GroupBy(r => (r.EmployeeId, r.ProjectId)))
        {
            var dayTypes = await _otRates.ResolveDayTypesAsync(
                group.Key.EmployeeId, start, end, group.Key.ProjectId);

            var normal = 0;
            var rest = 0;
            var holiday = 0;
            foreach (var request in group)
            {
                switch (dayTypes.GetValueOrDefault(request.WorkDate.Date, OtDayType.NORMAL_DAY))
                {
                    case OtDayType.PUBLIC_HOLIDAY: holiday += request.RequestedMinutes; break;
                    case OtDayType.REST_DAY: rest += request.RequestedMinutes; break;
                    default: normal += request.RequestedMinutes; break;
                }
            }

            var existing = result.GetValueOrDefault(group.Key.EmployeeId);
            result[group.Key.EmployeeId] = existing is null
                ? new ApprovedOvertimeMinutes(normal, rest, holiday)
                : new ApprovedOvertimeMinutes(
                    existing.NormalDayMin + normal,
                    existing.RestDayMin + rest,
                    existing.PublicHolidayMin + holiday);
        }

        return result;
    }
}
