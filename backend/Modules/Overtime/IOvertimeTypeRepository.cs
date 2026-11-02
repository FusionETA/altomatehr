using AltomateHR.Api.Modules.Overtime.Entities;

namespace AltomateHR.Api.Modules.Overtime;

public interface IOvertimeTypeRepository
{
    // Archived ones included: a payroll run still prices hours approved
    // before a type was archived.
    Task<List<OvertimeType>> GetAllAsync();
    Task<OvertimeType?> GetByIdAsync(string id);
    Task AddRangeAsync(IEnumerable<OvertimeType> types);
    Task UpdateAsync(OvertimeType type);
}
