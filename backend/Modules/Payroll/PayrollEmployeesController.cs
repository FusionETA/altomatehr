using AltomateHR.Api.Common.Tabular;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltomateHR.Api.Modules.Payroll;

// Employees' payroll details: the roster, and bulk-filling it.
//
// Read the list, or export → edit in a spreadsheet → import back. Admin-only:
// these fields decide what everyone is paid and what is filed for them.
[ApiController]
[Route("payroll/employees")]
[Authorize(Roles = "Admin,Owner")]
public class PayrollEmployeesController : ControllerBase
{
    private readonly IPayrollEmployeeImportService _import;
    private readonly IPayrollEmployeeDirectoryService _roster;

    public PayrollEmployeesController(
        IPayrollEmployeeImportService import, IPayrollEmployeeDirectoryService roster)
    {
        _import = import;
        _roster = roster;
    }

    // The roster, with each person's statutory gaps already worked out — the
    // same gaps a run's readiness check reports, so they can be cleared
    // before a run exists rather than when one refuses to submit.
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] bool includeArchived = false) =>
        Ok(await _roster.GetAllAsync(includeArchived));

    [HttpGet("template")]
    public IActionResult Template([FromQuery] TabularFormat format = TabularFormat.Xlsx)
    {
        var result = _import.BuildTemplate(format);
        return File(result.Content, result.ContentType, result.FileName);
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] TabularFormat format = TabularFormat.Xlsx)
    {
        var result = await _import.ExportAsync(format);
        return File(result.Content, result.ContentType, result.FileName);
    }

    // Per-row success and failure, so the response is a report rather than
    // one pass/fail for the whole file — the same contract the attendance,
    // leave and claims imports use.
    [HttpPost("import")]
    public async Task<IActionResult> Import(IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { error = "No file was uploaded." });
        }

        var format = Path.GetExtension(file.FileName).ToLowerInvariant() switch
        {
            ".csv" => TabularFormat.Csv,
            _ => TabularFormat.Xlsx,
        };

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer);

        return Ok(await _import.ImportAsync(buffer.ToArray(), format));
    }
}
