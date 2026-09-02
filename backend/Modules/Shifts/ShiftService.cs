using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Projects;
using AltomateHR.Api.Modules.Shifts.Dtos;
using AltomateHR.Api.Modules.Shifts.Entities;

namespace AltomateHR.Api.Modules.Shifts;

// Shift CRUD + "exactly one default per project" invariant. Mirrors
// Modules/Policies/PolicyService's IsDefault/ClearDefaultExcept pattern.
public class ShiftService : IShiftService
{
    private readonly IDirectoryService _directory;
    private readonly IShiftRepository _shifts;
    private readonly IProjectService _projects;

    public ShiftService(IShiftRepository shifts, IProjectService projects, IDirectoryService directory)
    {
        _shifts = shifts;
        _projects = projects;
        _directory = directory;
    }

    public async Task<IEnumerable<ShiftDto>> GetAllAsync() =>
        (await _shifts.GetAllAsync()).Select(ToDto);

    public async Task<IEnumerable<ShiftDto>> GetForProjectAsync(string projectId) =>
        (await _shifts.GetForProjectAsync(projectId)).Select(ToDto);

    public async Task<ShiftSaveResult> CreateAsync(CreateShiftDto dto)
    {
        if (await _projects.GetByIdAsync(dto.ProjectId) is null)
            return new ShiftSaveResult(false, null, "Project not found.");

        var name = dto.Name.Trim();
        if (await _shifts.GetByNameAsync(dto.ProjectId, name) is not null)
            return new ShiftSaveResult(false, null, $"A shift named \"{name}\" already exists for this project.");

        // No StartTime < EndTime check here (unlike org working hours) — a night
        // shift crossing midnight (e.g. 22:00-06:00) is a valid, real-world shift.

        var now = DateTime.UtcNow;
        var shift = new Shift
        {
            ProjectId = dto.ProjectId,
            Name = name,
            StartTime = dto.StartTime,
            EndTime = dto.EndTime,
            WorkingDays = string.IsNullOrWhiteSpace(dto.WorkingDays) ? null : dto.WorkingDays,
            LunchBreakMinutes = dto.LunchBreakMinutes,
            // First shift for a project becomes its default automatically.
            IsDefault = await _shifts.GetDefaultForProjectAsync(dto.ProjectId) is null,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var saved = await _shifts.AddAsync(shift);
        return new ShiftSaveResult(true, ToDto(saved));
    }

    public async Task<ShiftSaveResult> UpdateAsync(string id, UpdateShiftDto dto)
    {
        var shift = await _shifts.GetByIdAsync(id);
        if (shift is null) return new ShiftSaveResult(false, null, null);   // → 404

        var name = dto.Name.Trim();
        var clash = await _shifts.GetByNameAsync(shift.ProjectId, name);
        if (clash is not null && clash.Id != id)
            return new ShiftSaveResult(false, null, $"A shift named \"{name}\" already exists for this project.");

        shift.Name = name;
        shift.StartTime = dto.StartTime;
        shift.EndTime = dto.EndTime;
        shift.WorkingDays = string.IsNullOrWhiteSpace(dto.WorkingDays) ? null : dto.WorkingDays;
        shift.LunchBreakMinutes = dto.LunchBreakMinutes;
        shift.UpdatedAt = DateTime.UtcNow;
        await _shifts.UpdateAsync(shift);

        return new ShiftSaveResult(true, ToDto(shift));
    }

    public async Task<ShiftDeleteResult> DeleteAsync(string id)
    {
        var shift = await _shifts.GetByIdAsync(id);
        if (shift is null) return new ShiftDeleteResult(false, null, "NOT_FOUND");

        var assignedCount = await _directory.CountMembershipsByShiftAsync(id);
        if (assignedCount > 0)
            return new ShiftDeleteResult(false,
                $"Can't delete — {assignedCount} employee(s) still assigned to this shift. Reassign them first.",
                "IN_USE", assignedCount);

        await _shifts.DeleteAsync(shift);
        return new ShiftDeleteResult(true);
    }

    public async Task<ShiftSaveResult> SetDefaultAsync(string id)
    {
        var shift = await _shifts.GetByIdAsync(id);
        if (shift is null) return new ShiftSaveResult(false, null, null);   // → 404

        shift.IsDefault = true;
        shift.UpdatedAt = DateTime.UtcNow;
        await _shifts.UpdateAsync(shift);
        await _shifts.ClearDefaultForProjectExceptAsync(shift.ProjectId, shift.Id);   // exactly one default per project

        return new ShiftSaveResult(true, ToDto(shift));
    }

    public async Task<Shift?> GetEffectiveShiftAsync(string employeeId)
    {
        var membership = await _directory.GetMembershipForUserAsync(employeeId);
        if (membership?.ShiftId is not null)
        {
            var assigned = await _shifts.GetByIdAsync(membership.ShiftId);
            if (assigned is not null) return assigned;
        }

        // No explicit assignment (or it points at a deleted shift) — fall back
        // to the default shift of whichever project the employee's shift would
        // belong to. Without a project association on the membership there's
        // nothing further to resolve, so return null and let the caller use the
        // org working hours.
        return null;
    }

    private static ShiftDto ToDto(Shift s) => new()
    {
        Id = s.Id,
        ProjectId = s.ProjectId,
        Name = s.Name,
        StartTime = s.StartTime,
        EndTime = s.EndTime,
        WorkingDays = s.WorkingDays,
        LunchBreakMinutes = s.LunchBreakMinutes,
        IsDefault = s.IsDefault,
    };

    public Task<Shift?> GetByIdAsync(string id) => _shifts.GetByIdAsync(id);
}
