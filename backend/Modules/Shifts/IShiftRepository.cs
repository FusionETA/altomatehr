using AltomateHR.Api.Modules.Shifts.Entities;

namespace AltomateHR.Api.Modules.Shifts;

public interface IShiftRepository
{
    Task<List<Shift>> GetAllAsync();
    Task<List<Shift>> GetForProjectAsync(string projectId);
    Task<Shift?> GetByIdAsync(string id);
    Task<Shift?> GetByNameAsync(string projectId, string name);
    Task<Shift?> GetDefaultForProjectAsync(string projectId);
    Task<Shift> AddAsync(Shift shift);
    Task UpdateAsync(Shift shift);
    Task DeleteAsync(Shift shift);
    Task ClearDefaultForProjectExceptAsync(string projectId, string keepId);
}
