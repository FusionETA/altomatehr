using AltomateHR.Api.Modules.Shifts.Dtos;
using AltomateHR.Api.Modules.Shifts.Entities;

namespace AltomateHR.Api.Modules.Shifts;

public interface IShiftService
{
    Task<IEnumerable<ShiftDto>> GetAllAsync();
    Task<IEnumerable<ShiftDto>> GetForProjectAsync(string projectId);
    Task<ShiftSaveResult> CreateAsync(CreateShiftDto dto);
    Task<ShiftSaveResult> UpdateAsync(string id, UpdateShiftDto dto);
    Task<ShiftDeleteResult> DeleteAsync(string id);
    Task<ShiftSaveResult> SetDefaultAsync(string id);

    // Soft-archive / restore. Archiving also clears IsDefault — an archived
    // shift can't be a project's fallback — so a project left with no default
    // resolves to the org working hours until another shift is promoted.
    Task<ShiftSaveResult> SetArchivedAsync(string id, bool archived);

    // The shift governing this employee: their explicitly assigned one, else
    // their project's default. Null when neither exists (callers fall back to
    // the org's working hours).
    Task<Shift?> GetEffectiveShiftAsync(string employeeId);

    // A shift by id, as an entity. GetEffectiveShiftAsync resolves the whole
    // assigned/project/org chain; this is the plain lookup for a known id.
    Task<Shift?> GetByIdAsync(string id);
}

// Ok=false, Error=null → 404 (not found). Ok=false, Error!=null → 400 (validation).
public record ShiftSaveResult(bool Ok, ShiftDto? Shift, string? Error = null);

public record ShiftDeleteResult(bool Ok, string? Error = null, string? Code = null, int? AssignedCount = null);
