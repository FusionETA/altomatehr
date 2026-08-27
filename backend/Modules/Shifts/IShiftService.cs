using AltomateHR.Api.Modules.Shifts.Dtos;

namespace AltomateHR.Api.Modules.Shifts;

public interface IShiftService
{
    Task<IEnumerable<ShiftDto>> GetAllAsync();
    Task<IEnumerable<ShiftDto>> GetForProjectAsync(string projectId);
    Task<ShiftSaveResult> CreateAsync(CreateShiftDto dto);
    Task<ShiftSaveResult> UpdateAsync(string id, UpdateShiftDto dto);
    Task<ShiftDeleteResult> DeleteAsync(string id);
    Task<ShiftSaveResult> SetDefaultAsync(string id);
}

// Ok=false, Error=null → 404 (not found). Ok=false, Error!=null → 400 (validation).
public record ShiftSaveResult(bool Ok, ShiftDto? Shift, string? Error = null);

public record ShiftDeleteResult(bool Ok, string? Error = null, string? Code = null, int? AssignedCount = null);
