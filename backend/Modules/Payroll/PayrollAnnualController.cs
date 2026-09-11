using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltomateHR.Api.Modules.Payroll;

// The year-end filings.
//
// Admin-only: these are the employer's return and every employee's statement
// of remuneration in one document. An employee's own EA reaches them through
// the payslips surface, not here.
[ApiController]
[Route("payroll/annual")]
[Authorize(Roles = "Admin,Owner")]
public class PayrollAnnualController : ControllerBase
{
    private readonly IPayrollAnnualReportService _annual;

    public PayrollAnnualController(IPayrollAnnualReportService annual) => _annual = annual;

    // What can be produced. Independent of the year, so the page can render
    // its list before picking one.
    [HttpGet("reports")]
    public IActionResult Available() => Ok(_annual.GetAvailable());

    // The aggregated year — what the forms will say, before downloading them.
    [HttpGet("{year:int}")]
    public async Task<IActionResult> Get(int year) => Ok(await _annual.LoadAsync(year));

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
