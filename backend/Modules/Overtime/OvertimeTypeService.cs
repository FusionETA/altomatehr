using AltomateHR.Api.Modules.Overtime.Dtos;
using AltomateHR.Api.Modules.Overtime.Entities;
using AltomateHR.Api.Modules.Policies;
using AltomateHR.Api.Modules.Policies.Entities;

namespace AltomateHR.Api.Modules.Overtime;

public class OvertimeTypeService : IOvertimeTypeService
{
    // The Employment Act 1955 s.60A floors, as every company starts.
    public static readonly IReadOnlyList<(OvertimeTypeKind Kind, string Name, decimal Multiplier)> BuiltIns =
    [
        (OvertimeTypeKind.NORMAL_DAY, "Normal day", 1.50m),
        (OvertimeTypeKind.REST_DAY, "Rest day", 2.00m),
        (OvertimeTypeKind.PUBLIC_HOLIDAY, "Public holiday", 3.00m),
    ];

    private readonly IOvertimeTypeRepository _types;
    private readonly IPolicyOvertimeRateRepository _policyRates;
    private readonly IPolicyService _policies;
    private readonly Payroll.IPayrollDraftStaleness? _drafts;

    // Loaded once per request: a payroll run prices every employee off them.
    private List<OvertimeType>? _loaded;
    private ILookup<string, PolicyOvertimeRate>? _ratesByPolicy;

    public OvertimeTypeService(
        IOvertimeTypeRepository types,
        IPolicyOvertimeRateRepository policyRates,
        IPolicyService policies,
        Payroll.IPayrollDraftStaleness? drafts = null)
    {
        _drafts = drafts;
        _types = types;
        _policyRates = policyRates;
        _policies = policies;
    }

    public async Task<IReadOnlyList<OvertimeTypeDto>> GetAllAsync(bool includeArchived = false) =>
        [.. (await TypesAsync()).Where(t => includeArchived || !t.IsArchived).Select(ToDto)];

    public async Task<OvertimeTypeSaveResult> CreateAsync(SaveOvertimeTypeDto dto)
    {
        var types = await TypesAsync();
        var name = dto.Name.Trim();
        if (NameTaken(types, name, exceptId: null) is { } clash) return clash;

        var now = DateTime.UtcNow;
        var type = new OvertimeType
        {
            Name = name,
            Kind = OvertimeTypeKind.CUSTOM,
            Multiplier = dto.Multiplier!.Value,
            SortOrder = types.Count(t => t.Kind == OvertimeTypeKind.CUSTOM),
            CreatedAt = now,
            UpdatedAt = now,
        };
        await _types.AddRangeAsync([type]);
        types.Add(type);
        return new OvertimeTypeSaveResult(true, ToDto(type), null);
    }

    public async Task<OvertimeTypeSaveResult> UpdateAsync(string id, SaveOvertimeTypeDto dto)
    {
        var types = await TypesAsync();
        var type = types.FirstOrDefault(t => t.Id == id);
        if (type is null) return new OvertimeTypeSaveResult(false, null, "No such overtime type.", NotFound: true);

        var name = dto.Name.Trim();
        if (NameTaken(types, name, exceptId: id) is { } clash) return clash;

        var rateChanged = type.Multiplier != dto.Multiplier;
        type.Name = name;
        type.Multiplier = dto.Multiplier!.Value;
        type.UpdatedAt = DateTime.UtcNow;
        await _types.UpdateAsync(type);

        // Drafts priced at the old rate must be re-run before they're filed.
        if (rateChanged && _drafts is not null) await _drafts.MarkAllDraftsAsync();
        return new OvertimeTypeSaveResult(true, ToDto(type), null);
    }

    public async Task<OvertimeTypeSaveResult> ArchiveAsync(string id)
    {
        var type = (await TypesAsync()).FirstOrDefault(t => t.Id == id);
        if (type is null) return new OvertimeTypeSaveResult(false, null, "No such overtime type.", NotFound: true);
        if (type.Kind != OvertimeTypeKind.CUSTOM)
            return new OvertimeTypeSaveResult(false, null,
                $"\"{type.Name}\" is built in: approved overtime lands in it by the calendar, so it can't be removed.");

        type.IsArchived = true;
        type.UpdatedAt = DateTime.UtcNow;
        await _types.UpdateAsync(type);
        return new OvertimeTypeSaveResult(true, ToDto(type), null);
    }

    public async Task<OvertimeRates> RatesForPolicyAsync(EmployeePolicy? policy)
    {
        var types = await TypesAsync();
        decimal Company(OvertimeTypeKind kind) =>
            types.FirstOrDefault(t => t.Kind == kind)?.Multiplier
            ?? BuiltIns.First(b => b.Kind == kind).Multiplier;

        var overrides = policy is null
            ? []
            : (await RatesByPolicyAsync())[policy.Id].ToDictionary(r => r.OvertimeTypeId, StringComparer.Ordinal);

        var custom = types
            .Where(t => t.Kind == OvertimeTypeKind.CUSTOM)
            .ToDictionary(
                t => t.Id,
                t =>
                {
                    var own = overrides.GetValueOrDefault(t.Id);
                    return new CustomOvertimeRate(
                        t.Id, t.Name, own?.Multiplier ?? t.Multiplier, !t.IsArchived && (own?.Enabled ?? true));
                },
                StringComparer.Ordinal);

        return new OvertimeRates(
            policy?.OtRateNormalDay ?? Company(OvertimeTypeKind.NORMAL_DAY),
            policy?.OtRateRestDay ?? Company(OvertimeTypeKind.REST_DAY),
            policy?.OtRatePublicHoliday ?? Company(OvertimeTypeKind.PUBLIC_HOLIDAY),
            custom);
    }

    public async Task<IReadOnlyList<PickableOvertimeTypeDto>> PickableForEmployeeAsync(string employeeId)
    {
        // Banked as time off, or switched off: nothing is paid at any rate,
        // so offering "Night shift 1.25×" would promise money that never comes.
        var policy = await _policies.GetEffectivePolicyAsync(employeeId);
        if (policy is not null && (!policy.OtEnabled || policy.OtMethod != OtMethod.CASH)) return [];

        var rates = await RatesForPolicyAsync(policy);
        return
        [
            .. rates.Custom.Values
                .Where(r => r.Offered)
                .Select(r => new PickableOvertimeTypeDto { Id = r.TypeId, Name = r.Name, Multiplier = r.Multiplier }),
        ];
    }

    // ---- Helpers ----

    // The company's types, with the three built-ins made on first use for a
    // company that has none yet (a new company gets them at creation).
    private async Task<List<OvertimeType>> TypesAsync()
    {
        if (_loaded is not null) return _loaded;

        var types = await _types.GetAllAsync();
        var missing = BuiltIns.Where(b => types.All(t => t.Kind != b.Kind)).ToList();
        if (missing.Count > 0)
        {
            var now = DateTime.UtcNow;
            var added = missing.Select(b => new OvertimeType
            {
                Name = b.Name,
                Kind = b.Kind,
                Multiplier = b.Multiplier,
                CreatedAt = now,
                UpdatedAt = now,
            }).ToList();
            await _types.AddRangeAsync(added);
            types = await _types.GetAllAsync();
        }

        // Built-ins in their own order (Kind is stored as text, so the database
        // can't sort by it), then the company's own as they were added.
        return _loaded = [.. types.OrderBy(t => (int)t.Kind).ThenBy(t => t.SortOrder).ThenBy(t => t.CreatedAt)];
    }

    private async Task<ILookup<string, PolicyOvertimeRate>> RatesByPolicyAsync() =>
        _ratesByPolicy ??= (await _policyRates.GetAllAsync()).ToLookup(r => r.PolicyId, StringComparer.Ordinal);

    private static OvertimeTypeSaveResult? NameTaken(IEnumerable<OvertimeType> types, string name, string? exceptId) =>
        types.Any(t => !t.IsArchived && t.Id != exceptId && string.Equals(t.Name.Trim(), name, StringComparison.OrdinalIgnoreCase))
            ? new OvertimeTypeSaveResult(false, null, $"There's already an overtime type called \"{name}\".")
            : null;

    private static OvertimeTypeDto ToDto(OvertimeType t) => new()
    {
        Id = t.Id,
        Name = t.Name,
        Kind = t.Kind,
        Multiplier = t.Multiplier,
        IsArchived = t.IsArchived,
    };
}
