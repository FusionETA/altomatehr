using AltomateHR.Api.Modules.ApiKeys;
using AltomateHR.Api.Modules.Payroll.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AltomateHR.Api.Modules.Organizations;

namespace AltomateHR.Api.Modules.Payroll;

// The year-end filings.
//
// Admin-only: these are the employer's return and every employee's statement
// of remuneration in one document. An employee's own EA reaches them through
// the payslips surface, not here.
[ApiController]
[Route("payroll/annual")]
[RequireFullEmployeeScope(IncludeReads = true)]   // annual returns cover every employee
[RequireModule(OrgModules.Payroll)]
[Authorize(Roles = "Admin,Owner")]
public class PayrollAnnualController : ControllerBase
{
    private readonly IPayrollAnnualReportService _annual;
    private readonly IPayrollSettingsService _settings;

    public PayrollAnnualController(IPayrollAnnualReportService annual, IPayrollSettingsService settings)
    {
        _annual = annual;
        _settings = settings;
    }

    // What can be produced. Independent of the year, so the page can render
    // its list before picking one.
    [RequireScope("payroll:read")]
    [HttpGet("reports")]
    public IActionResult Available() => Ok(_annual.GetAvailable());

    // The aggregated year — what the forms will say, before downloading them.
    [RequireScope("payroll:read")]
    [HttpGet("{year:int}")]
    public async Task<IActionResult> Get(int year) => Ok(await _annual.LoadAsync(year));

    // PUT /payroll/annual/start — "payroll at this company started in <month>
    // <year>", so that year's forms only wait for the months from then
    // through December. Both null clears it (January). 204; the page reloads
    // the year to see the effect.
    [RequireScope("payroll:write")]
    [RequireSettings]
    [HttpPut("start")]
    public async Task<IActionResult> SetStart(SetPayrollStartDto dto)
    {
        if ((dto.Year is null) != (dto.Month is null))
            return BadRequest(new { error = "Give both the year and the month, or neither." });

        await _settings.SetPayrollStartAsync(dto.Year, dto.Month);
        return NoContent();
    }

    // POST /payroll/annual/cp8d/convert — hand-entered rows in, the zipped
    // M + P pair out. Nothing is read from or written to payroll: this is for
    // the years the system did not run.
    [RequireScope("payroll:write")]
    [ReadOnlyAction]   // converts an uploaded file; changes nothing
    [HttpPost("cp8d/convert")]
    public IActionResult ConvertCp8d(Cp8dConvertRequestDto request)
    {
        var result = _annual.ConvertCp8d(request);
        if (!result.Ok)
            return result.Error is null ? NotFound() : Conflict(new { error = result.Error });

        Response.Headers.CacheControl = "no-store";
        return File(result.Content!, result.ContentType!, result.FileName);
    }

    [RequireScope("payroll:read")]
    [HttpGet("{year:int}/reports/{kind}")]
    public async Task<IActionResult> Download(int year, PayrollAnnualReportKind kind)
    {
        var result = await _annual.RenderAsync(kind, year);

        // A missing E-number is the admin's data to fix, so it is a 409 with
        // the specific reason rather than a 500 or a truncated file.
        if (!result.Ok)
        {
            return result.Error is null ? NotFound() : Conflict(new { error = result.Error });
        }

        return File(result.Content!, result.ContentType!, result.FileName);
    }
}
