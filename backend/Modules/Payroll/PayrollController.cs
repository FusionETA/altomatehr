using AltomateHR.Api.Modules.ApiKeys;
using AltomateHR.Api.Modules.Payroll.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AltomateHR.Api.Modules.Organizations;

namespace AltomateHR.Api.Modules.Payroll;

// Org-level payroll configuration.
//
// Admin-only throughout: these values decide what lands on a statutory filing
// and how much every employee is paid, so there is no employee-facing read.
[ApiController]
[Route("payroll")]
[RequireModule(OrgModules.Payroll)]
[Authorize(Roles = "Admin,Owner")]
public class PayrollController : ControllerBase
{
    private readonly IPayrollSettingsService _settings;
    private readonly IPayrollCompanyInfoService _companyInfo;
    private readonly IPayrollJournalSplitService _split;

    public PayrollController(
        IPayrollSettingsService settings, IPayrollCompanyInfoService companyInfo,
        IPayrollJournalSplitService split)
    {
        _settings = settings;
        _companyInfo = companyInfo;
        _split = split;
    }

    // The bank register the bank-file generators match employees against.
    // Served rather than copied into the client for the same reason as the
    // categories below: a bank picked from a second list that drifted would be
    // refused by the very file it was picked for, and one unmatched name
    // refuses the whole run's bank file.
    // A lookup other admin screens borrow (claims / attendance filters, the
    // employee form), so it is held to the plan only. See ModuleGrantExempt.
    [ModuleGrantExempt]
    [RequireScope("payroll:read")]
    [HttpGet("banks")]
    public IActionResult Banks() =>
        Ok(MalaysianBanks.All.Select(b => new { name = b.Name, aliases = b.Aliases }));

    // Both GETs return defaults rather than 404 for an org that hasn't
    // configured payroll yet — see the services for why.
    // The adjustment category catalogue. Static reference data, served rather
    // than duplicated in the client: the calculator dispatches on these codes,
    // and a second copy that drifted would describe a row's statutory
    // treatment wrongly or offer a code generation silently skips.
    [RequireScope("payroll:read")]
    [HttpGet("adjustment-categories")]
    public IActionResult AdjustmentCategories() =>
        Ok(PayrollAdjustmentCategories.All.Values.Select(meta => new PayrollAdjustmentCategoryDto
        {
            Code = meta.Code,
            Label = meta.Label,
            Kind = meta.Kind,
            SubjectToEpf = meta.SubjectToEpf,
            SubjectToSocso = meta.SubjectToSocso,
            SubjectToEis = meta.SubjectToEis,
            SubjectToPcb = meta.SubjectToPcb,
            SubjectToHrdf = meta.SubjectToHrdf,
            TaxExemptLimit = meta.TaxExemptLimit,
            ReducesBase = meta.ReducesBase,
            ReducesGross = meta.ReducesGross,
            CashNeutral = meta.CashNeutral,
            FeedsLp1Relief = meta.FeedsLp1Relief,
            AddsToCp38Field = meta.AddsToCp38Field,
            AddsToStandardPcb = meta.AddsToStandardPcb,
            IsAdditionalRemuneration = meta.IsAdditionalRemuneration,
            OffsetsPcb = meta.OffsetsPcb,
            NonCash = meta.NonCash,
            // The code prefix IS the grouping — the catalogue is declared in
            // these four blocks, so deriving it cannot fall out of step with
            // a hand-maintained second list.
            Group = meta.Code.Split('_')[0] switch
            {
                "allowance" => "ALLOWANCE",
                "wages" => "REMUNERATION",
                "bik" => "BENEFIT_IN_KIND",
                _ => "DEDUCTION",
            },
        }));

    [RequireScope("payroll:read")]
    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings() => Ok(await _settings.GetAsync());

    [RequireScope("payroll:write")]
    [RequireSettings]
    [HttpPut("settings")]
    public async Task<IActionResult> SaveSettings(SavePayrollSettingsDto dto) =>
        Ok(await _settings.SaveAsync(dto));

    // Payslip passwords: on/off and what they are. Its own endpoint so the
    // main settings save (a full replace) can't switch it off by omission.
    [RequireScope("payroll:write")]
    [RequireSettings]
    [HttpPut("settings/payslip-password")]
    public async Task<IActionResult> SavePayslipPassword(SavePayslipPasswordDto dto)
    {
        var result = await _settings.SavePayslipPasswordAsync(dto);
        return result.Error is null ? Ok(result.Settings) : BadRequest(new { error = result.Error });
    }

    // The company payslip password in clear (COMPANY rule). Its own endpoint
    // behind an explicit click, like the portal logins, and audited — it opens
    // every payslip in the company. `payroll:read` like every payroll GET (and
    // the portal-login reveal); RequireSettings keeps it to admins who can
    // change payroll settings.
    [RequireScope("payroll:read")]
    [RequireSettings]
    [HttpGet("settings/payslip-password/reveal")]
    public async Task<IActionResult> RevealPayslipPassword()
    {
        var password = await _settings.RevealPayslipPasswordAsync();
        return password is null ? NotFound() : Ok(new PayslipCompanyPasswordDto { Password = password });
    }

    // Xero sync → AI split. Generate turns the admin's words into rules with
    // AI and tries them on a real run; Check re-tries rules already in hand
    // without AI. Neither saves anything — the rules are saved with the rest
    // of the settings. Same gate as saving the settings.
    [RequireScope("payroll:write")]
    [RequireSettings]
    [HttpPost("settings/xero/split/generate")]
    public async Task<IActionResult> GenerateXeroSplit(GenerateXeroSplitDto dto, CancellationToken ct)
    {
        var result = await _split.GenerateAsync(dto, ct);
        return result.Error is null ? Ok(result) : Conflict(new { error = result.Error });
    }

    [RequireScope("payroll:write")]
    [RequireSettings]
    [HttpPost("settings/xero/split/check")]
    public async Task<IActionResult> CheckXeroSplit(CheckXeroSplitDto dto) =>
        Ok(await _split.CheckAsync(dto));

    [RequireScope("payroll:read")]
    [HttpGet("company-info")]
    public async Task<IActionResult> GetCompanyInfo() => Ok(await _companyInfo.GetAsync());

    [RequireScope("payroll:write")]
    [RequireSettings]
    [HttpPut("company-info")]
    public async Task<IActionResult> SaveCompanyInfo(SavePayrollCompanyInfoDto dto) =>
        Ok(await _companyInfo.SaveAsync(dto));
}
