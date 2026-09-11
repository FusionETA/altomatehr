using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Payroll.Dtos;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

public class PayrollSettingsService : IPayrollSettingsService
{
    private readonly IPayrollSettingsRepository _repo;
    private readonly IAuditService _audit;

    public PayrollSettingsService(IPayrollSettingsRepository repo, IAuditService audit)
    {
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

        return settings is null ? Defaults() : ToDto(settings);
    }

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

        return ToDto(settings);
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

    private static PayrollSettingsDto ToDto(PayrollSettings s, bool isConfigured = true) => new()
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
