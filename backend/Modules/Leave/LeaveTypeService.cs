using AltomateHR.Api.Modules.Leave.Dtos;
using AltomateHR.Api.Modules.Leave.Entities;

namespace AltomateHR.Api.Modules.Leave;

public class LeaveTypeService : ILeaveTypeService
{
    private readonly ILeaveTypeRepository _repo;

    public LeaveTypeService(ILeaveTypeRepository repo) => _repo = repo;

    public async Task<IEnumerable<LeaveTypeDto>> GetAllAsync() =>
        (await _repo.GetAllAsync()).Select(ToDto);

    public async Task<LeaveTypeSaveResult> CreateAsync(SaveLeaveTypeDto dto)
    {
        var code = dto.Code.Trim();
        if (await _repo.GetByCodeAsync(code) is not null)
            return new LeaveTypeSaveResult(false, null, $"A leave type with code \"{code}\" already exists.");

        var now = DateTime.UtcNow;
        var type = new LeaveType
        {
            Code = code,
            Name = dto.Name.Trim(),
            Paid = dto.Paid,
            DefaultDays = dto.DefaultDays,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await _repo.AddAsync(type);
        return new LeaveTypeSaveResult(true, ToDto(type), null);
    }

    public async Task<LeaveTypeSaveResult> UpdateAsync(string id, SaveLeaveTypeDto dto)
    {
        var type = await _repo.GetByIdAsync(id);
        if (type is null)
            return new LeaveTypeSaveResult(false, null, null);   // → 404 in the controller

        var code = dto.Code.Trim();
        var clash = await _repo.GetByCodeAsync(code);
        if (clash is not null && clash.Id != id)
            return new LeaveTypeSaveResult(false, null, $"A leave type with code \"{code}\" already exists.");

        type.Code = code;
        type.Name = dto.Name.Trim();
        type.Paid = dto.Paid;
        type.DefaultDays = dto.DefaultDays;
        type.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(type);
        return new LeaveTypeSaveResult(true, ToDto(type), null);
    }

    public async Task<LeaveTypeDto?> SetArchivedAsync(string id, bool archived)
    {
        var type = await _repo.GetByIdAsync(id);
        if (type is null) return null;

        type.IsArchived = archived;
        type.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(type);
        return ToDto(type);
    }

    private static LeaveTypeDto ToDto(LeaveType t) => new()
    {
        Id = t.Id,
        Code = t.Code,
        Name = t.Name,
        Paid = t.Paid,
        DefaultDays = t.DefaultDays,
        IsArchived = t.IsArchived,
    };
}
