using AltomateHR.Api.Modules.Overtime.Dtos;
using AltomateHR.Api.Modules.Policies.Entities;

namespace AltomateHR.Api.Modules.Overtime;

// The company's overtime types (three built in, any number of its own) and
// the multiplier each one pays for a given policy.
public interface IOvertimeTypeService
{
    // Built-ins first, then the company's own. Archived ones only when asked.
    Task<IReadOnlyList<OvertimeTypeDto>> GetAllAsync(bool includeArchived = false);

    // Owner only: these reach every policy.
    Task<OvertimeTypeSaveResult> CreateAsync(SaveOvertimeTypeDto dto);
    Task<OvertimeTypeSaveResult> UpdateAsync(string id, SaveOvertimeTypeDto dto);

    // Custom types only. Hours already approved for it are still paid.
    Task<OvertimeTypeSaveResult> ArchiveAsync(string id);

    // What each type pays for someone on this policy (null = no policy: the
    // company's rates). Every custom type is priced, archived or switched off
    // included, because hours approved for it are still owed; `Offered` says
    // whether it can be picked or typed in now.
    Task<OvertimeRates> RatesForPolicyAsync(EmployeePolicy? policy);

    // The custom types this employee may pick on an overtime request.
    Task<IReadOnlyList<PickableOvertimeTypeDto>> PickableForEmployeeAsync(string employeeId);
}

public record OvertimeTypeSaveResult(
    bool Ok, OvertimeTypeDto? Type, string? Error, bool NotFound = false);

public record OvertimeRates(
    decimal Normal, decimal Rest, decimal PublicHoliday, IReadOnlyDictionary<string, CustomOvertimeRate> Custom);

public record CustomOvertimeRate(string TypeId, string Name, decimal Multiplier, bool Offered);
