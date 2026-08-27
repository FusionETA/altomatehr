using AltomateHR.Api.Modules.Leave.Dtos;
using AltomateHR.Api.Modules.Leave.Entities;

namespace AltomateHR.Api.Modules.Leave;

public class LeaveTypeService : ILeaveTypeService
{
    private readonly ILeaveTypeRepository _repo;

    public LeaveTypeService(ILeaveTypeRepository repo) => _repo = repo;

    public async Task<IEnumerable<LeaveTypeDto>> GetAllAsync() =>
        (await _repo.GetAllAsync()).Select(ToDto);

    public async Task<int> EnsureDefaultsAsync()
    {
        var existing = (await _repo.GetAllAsync())
            .Select(t => t.Code.Trim().ToUpperInvariant())
            .ToHashSet();

        var now = DateTime.UtcNow;
        var added = 0;
        foreach (var seed in LeaveDefaults.All)
        {
            if (existing.Contains(seed.Code)) continue;
            await _repo.AddAsync(new LeaveType
            {
                Code = seed.Code,
                Name = seed.Name,
                Paid = seed.Paid,
                DefaultDays = seed.DefaultDays,
                AccrualMethod = LeaveAccrualMethod.LUMP_SUM,
                CreatedAt = now,
                UpdatedAt = now,
            });
            added++;
        }
        return added;
    }

    public async Task<LeaveTypeSaveResult> CreateAsync(SaveLeaveTypeDto dto)
    {
        if (Validate(dto) is { } invalid)
            return new LeaveTypeSaveResult(false, null, invalid);

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
            AccrualMethod = dto.AccrualMethod,
            CarryForward = dto.CarryForward,
            CarryExpiryMonth = dto.CarryExpiryMonth,
            MaxCarryForwardDays = dto.MaxCarryForwardDays,
            ProrateFirstYear = dto.ProrateFirstYear,
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

        if (Validate(dto) is { } invalid)
            return new LeaveTypeSaveResult(false, null, invalid);

        var code = dto.Code.Trim();
        var clash = await _repo.GetByCodeAsync(code);
        if (clash is not null && clash.Id != id)
            return new LeaveTypeSaveResult(false, null, $"A leave type with code \"{code}\" already exists.");

        type.Code = code;
        type.Name = dto.Name.Trim();
        type.Paid = dto.Paid;
        type.DefaultDays = dto.DefaultDays;
        type.AccrualMethod = dto.AccrualMethod;
        type.CarryForward = dto.CarryForward;
        type.CarryExpiryMonth = dto.CarryExpiryMonth;
        type.MaxCarryForwardDays = dto.MaxCarryForwardDays;
        type.ProrateFirstYear = dto.ProrateFirstYear;
        type.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(type);
        return new LeaveTypeSaveResult(true, ToDto(type), null);
    }

    public async Task<LeaveTypeSaveResult> SetArchivedAsync(string id, bool archived)
    {
        var type = await _repo.GetByIdAsync(id);
        if (type is null) return new LeaveTypeSaveResult(false, null, null);   // → 404

        if (archived && IsProtected(type.Code))
            return new LeaveTypeSaveResult(false, null, "UNPAID leave cannot be archived");

        type.IsArchived = archived;
        type.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(type);
        return new LeaveTypeSaveResult(true, ToDto(type), null);
    }


    // Only ANNUAL leave accrues monthly or carries forward. Ported verbatim
    // from production's validateLeaveTypeInput — the ANNUAL restriction is a
    // business rule, not an oversight: statutory leave types (medical,
    // maternity…) are granted in full and don't roll over.
    private static bool IsAnnualCode(string? code) =>
        (code ?? string.Empty).Trim().ToUpperInvariant() == LeaveDefaults.AnnualCode;

    private static string? Validate(SaveLeaveTypeDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Code)) return "Code is required";
        if (string.IsNullOrWhiteSpace(dto.Name)) return "Name is required";

        var isAnnual = IsAnnualCode(dto.Code);

        if (dto.AccrualMethod == LeaveAccrualMethod.PRO_RATED && !isAnnual)
            return "Pro-rated accrual is only allowed for ANNUAL leave";

        if (dto.CarryForward && !isAnnual)
            return "Carry-forward is only allowed for ANNUAL leave";

        if (dto.CarryForward &&
            (dto.CarryExpiryMonth is null || dto.CarryExpiryMonth < 1 || dto.CarryExpiryMonth > 12))
            return "Carry-forward requires an expiry month (1-12)";

        if (!dto.Paid && dto.DefaultDays != 0)
            return "Unpaid leave cannot have entitlement days";

        if (dto.DefaultDays < 0) return "Default days cannot be negative";

        if (dto.MaxCarryForwardDays is < 0) return "Max carry-forward days cannot be negative";

        return null;
    }

    // UNPAID is structural: the apply path exempts unpaid types from the
    // balance check, so removing it would strip that escape hatch.
    private static bool IsProtected(string? code) => LeaveDefaults.IsProtected(code);

    private static LeaveTypeDto ToDto(LeaveType t) => new()
    {
        Id = t.Id,
        Code = t.Code,
        Name = t.Name,
        Paid = t.Paid,
        DefaultDays = t.DefaultDays,
        IsArchived = t.IsArchived,
        AccrualMethod = t.AccrualMethod,
        CarryForward = t.CarryForward,
        CarryExpiryMonth = t.CarryExpiryMonth,
        MaxCarryForwardDays = t.MaxCarryForwardDays,
        ProrateFirstYear = t.ProrateFirstYear,
    };
}
