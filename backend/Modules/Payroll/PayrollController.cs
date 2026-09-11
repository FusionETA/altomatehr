using AltomateHR.Api.Modules.Payroll.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltomateHR.Api.Modules.Payroll;

// Org-level payroll configuration.
//
// Admin-only throughout: these values decide what lands on a statutory filing
// and how much every employee is paid, so there is no employee-facing read.
[ApiController]
[Route("payroll")]
[Authorize(Roles = "Admin,Owner")]
public class PayrollController : ControllerBase
{
    private readonly IPayrollSettingsService _settings;
    private readonly IPayrollCompanyInfoService _companyInfo;

    public PayrollController(
        IPayrollSettingsService settings, IPayrollCompanyInfoService companyInfo)
    {
        _settings = settings;
        _companyInfo = companyInfo;
    }

    // Both GETs return defaults rather than 404 for an org that hasn't
    // configured payroll yet — see the services for why.
    // The adjustment category catalogue. Static reference data, served rather
    // than duplicated in the client: the calculator dispatches on these codes,
    // and a second copy that drifted would describe a row's statutory
    // treatment wrongly or offer a code generation silently skips.
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

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings() => Ok(await _settings.GetAsync());

    [HttpPut("settings")]
    public async Task<IActionResult> SaveSettings(SavePayrollSettingsDto dto) =>
        Ok(await _settings.SaveAsync(dto));

    [HttpGet("company-info")]
    public async Task<IActionResult> GetCompanyInfo() => Ok(await _companyInfo.GetAsync());

    [HttpPut("company-info")]
    public async Task<IActionResult> SaveCompanyInfo(SavePayrollCompanyInfoDto dto) =>
        Ok(await _companyInfo.SaveAsync(dto));
}
