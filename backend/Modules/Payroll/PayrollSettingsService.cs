using AltomateHR.Api.Modules.ApiKeys;
using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Payroll.Dtos;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

public class PayrollSettingsService : IPayrollSettingsService
{
    private readonly IPayrollSettingsRepository _repo;
    private readonly IAuditService _audit;
    // Optional so hand-built instances in tests need not supply it; the app
    // always does. See PayrollDraftStaleness for why saves here mark drafts.
    private readonly IPayrollDraftStaleness? _drafts;
    // For the read-only AbPayEnabled flag. Optional for the same reason; with
    // none, the flag reads false (the AB Pay export is not offered).
    private readonly IApiKeyService? _apiKeys;

    public PayrollSettingsService(
        IPayrollSettingsRepository repo, IAuditService audit, IPayrollDraftStaleness? drafts = null,
        IApiKeyService? apiKeys = null)
    {
        _drafts = drafts;
        _apiKeys = apiKeys;
        _repo = repo;
        _audit = audit;
    }

    // An org with no row yet gets the statutory defaults rather than a 404 —
    // "not configured" is a valid state, and the calc engine has to run for a
    // brand-new org anyway. `IsConfigured` tells the UI which it is looking at.
    //
    // Deliberately does NOT create the row: a GET must not write.
    public async Task<PayrollSettingsDto> GetAsync()
    {
        var settings = await _repo.GetAsync();

        var dto = settings is null ? Defaults() : ToDto(settings, settings.ConfiguredAt is not null);
        dto.AbPayEnabled = await AbPayEnabledAsync();
        return dto;
    }

    // Follows the org's API keys, not anything saved here — see
    // IApiKeyService.HasAbPayIntegrationAsync.
    private async Task<bool> AbPayEnabledAsync() =>
        _apiKeys is not null && await _apiKeys.HasAbPayIntegrationAsync();

    // The entity the calc engine needs, materialised from defaults when the org
    // hasn't configured anything. Callers inside payroll use this rather than
    // the DTO so they don't have to re-map.
    public async Task<PayrollSettings> GetEffectiveAsync() =>
        await _repo.GetAsync() ?? new PayrollSettings();

    public async Task<PayrollSettingsDto> SaveAsync(SavePayrollSettingsDto dto)
    {
        var settings = await _repo.GetAsync();
        var isFirstSave = settings is null;
        var now = DateTime.UtcNow;

        settings ??= new PayrollSettings { CreatedAt = now };

        Apply(settings, dto);
        settings.ConfiguredAt ??= now;
        settings.UpdatedAt = now;

        if (isFirstSave)
        {
            await _repo.AddAsync(settings);
        }
        else
        {
            await _repo.UpdateAsync(settings);
        }

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.PayrollSettingsUpdate,
            isFirstSave ? "Configured payroll settings" : "Updated payroll settings",
            TargetType: "PayrollSettings",
            TargetId: settings.Id,
            Metadata: new
            {
                settings.WorkingDaysRule,
                settings.DefaultEpfEmployeeRate,
                settings.DefaultEpfEmployerRate,
                settings.HrdfEnabled,
                settings.HrdfRate,
                settings.AutoApplySocsoEisRelief,
            }));

        // EPF rates, the working-days rule and HRDF all change what a draft
        // computes.
        if (_drafts is not null) await _drafts.MarkAllDraftsAsync();

        var saved = ToDto(settings, isConfigured: true);
        saved.AbPayEnabled = await AbPayEnabledAsync();
        return saved;
    }

    public async Task SetPayrollStartAsync(int? year, int? month)
    {
        if ((year is null) != (month is null))
            throw new ArgumentException("Give both the year and the month, or neither.");

        var settings = await _repo.GetAsync();
        var isFirstSave = settings is null;
        var now = DateTime.UtcNow;
        // Creating the row here leaves ConfiguredAt null: setting the start
        // month is not a review of the payroll defaults.
        settings ??= new PayrollSettings { CreatedAt = now };

        // January is the default, so "started in January" is the same as unset.
        var clear = year is null || month == 1;
        settings.PayrollStartYear = clear ? null : year;
        settings.PayrollStartMonth = clear ? null : month;
        settings.UpdatedAt = now;

        if (isFirstSave) await _repo.AddAsync(settings);
        else await _repo.UpdateAsync(settings);

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.PayrollSettingsUpdate,
            clear
                ? "Cleared when payroll started here (year-end forms need January–December)"
                : $"Set payroll as started here in {month:D2}/{year} (year-end forms)",
            TargetType: "PayrollSettings",
            TargetId: settings.Id,
            Metadata: new { settings.PayrollStartYear, settings.PayrollStartMonth }));
    }

    private static void Apply(PayrollSettings settings, SavePayrollSettingsDto dto)
    {
        settings.WorkingDaysRule = dto.WorkingDaysRule;
        settings.DefaultEpfEmployeeRate = dto.DefaultEpfEmployeeRate;
        settings.DefaultEpfEmployerRate = dto.DefaultEpfEmployerRate;

        settings.HrdfEnabled = dto.HrdfEnabled;
        // Keeping a stale rate on a disabled levy invites it silently coming
        // back to life when someone flips the toggle later.
        settings.HrdfRate = dto.HrdfEnabled ? dto.HrdfRate : null;

        settings.AutoApplySocsoEisRelief = dto.AutoApplySocsoEisRelief;

        settings.SyncClaimsToXeroOnSubmit = dto.SyncClaimsToXeroOnSubmit;
        settings.SyncPayrollToXeroOnSubmit = dto.SyncPayrollToXeroOnSubmit;
        settings.XeroMappingJson = dto.XeroMappingJson;

        settings.PayrollBankName = dto.PayrollBankName;
        settings.PayorAccountHolderName = dto.PayorAccountHolderName;
        settings.PayorOrganisationCode = dto.PayorOrganisationCode;
        settings.EcpPayorAccountNo = dto.EcpPayorAccountNo;
        settings.EcpPayorBic = dto.EcpPayorBic;
    }

    // The entity's own field initialisers are the single source of truth for
    // what "unconfigured" means, so the defaults DTO is built from a fresh one.
    private static PayrollSettingsDto Defaults() => ToDto(new PayrollSettings(), isConfigured: false);

    private static PayrollSettingsDto ToDto(PayrollSettings s, bool isConfigured) => new()
    {
        WorkingDaysRule = s.WorkingDaysRule,
        DefaultEpfEmployeeRate = s.DefaultEpfEmployeeRate,
        DefaultEpfEmployerRate = s.DefaultEpfEmployerRate,
        HrdfEnabled = s.HrdfEnabled,
        HrdfRate = s.HrdfRate,
        AutoApplySocsoEisRelief = s.AutoApplySocsoEisRelief,
        SyncClaimsToXeroOnSubmit = s.SyncClaimsToXeroOnSubmit,
        SyncPayrollToXeroOnSubmit = s.SyncPayrollToXeroOnSubmit,
        XeroMappingJson = s.XeroMappingJson,
        PayrollBankName = s.PayrollBankName,
        PayorAccountHolderName = s.PayorAccountHolderName,
        PayorOrganisationCode = s.PayorOrganisationCode,
        EcpPayorAccountNo = s.EcpPayorAccountNo,
        EcpPayorBic = s.EcpPayorBic,
        IsConfigured = isConfigured,
        UpdatedAt = isConfigured ? s.UpdatedAt : null,
    };
}
