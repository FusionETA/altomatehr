using AltomateHR.Api.Modules.ApiKeys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AltomateHR.Api.Modules.Organizations;

namespace AltomateHR.Api.Modules.Payroll;

// One employee's salary history.
//
// A change is RECORDED as a side effect of editing the employee's salary —
// on their record, or many at once through the salary adjustment import —
// so the history and the salary can never disagree. A history that could be
// written independently of the profile would be a second source of truth for
// what someone earns.
[ApiController]
[Route("payroll/salary-changes")]
[EmployeeInScope("employeeProfileId")]
[RequireModule(OrgModules.Payroll)]
[Authorize(Roles = "Admin,Owner")]
public class SalaryChangesController : ControllerBase
{
    private readonly ISalaryChangeService _changes;
    private readonly ISalaryAdjustmentImportService _import;

    public SalaryChangesController(ISalaryChangeService changes, ISalaryAdjustmentImportService import)
    {
        _changes = changes;
        _import = import;
    }

    // GET /payroll/salary-changes/import/template — every payroll employee's
    // current salary, with the new-salary columns to fill in.
    [RequireScope("payroll:read")]
    [HttpGet("import/template")]
    public async Task<IActionResult> ImportTemplate()
    {
        var result = await _import.BuildTemplateAsync();
        Response.Headers.CacheControl = "no-store";
        return File(result.Content, result.ContentType, result.FileName);
    }

    // POST /payroll/salary-changes/import — new salaries for many people at
    // once, each recorded in the history. All or nothing.
    [RequireScope("payroll:write")]
    [RequireFullEmployeeScope]
    [HttpPost("import")]
    public async Task<IActionResult> Import(IFormFile? file)
    {
        if (file is null || file.Length == 0) return BadRequest(new { error = "No file was uploaded." });

        var format = Path.GetExtension(file.FileName).ToLowerInvariant() == ".csv"
            ? Common.Tabular.TabularFormat.Csv
            : Common.Tabular.TabularFormat.Xlsx;

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer);

        var result = await _import.ImportAsync(buffer.ToArray(), format);
        return result.Ok ? Ok(result) : BadRequest(result);
    }

    [RequireScope("payroll:read")]
    [HttpGet("{employeeProfileId}")]
    public async Task<IActionResult> GetForEmployee(string employeeProfileId) =>
        Ok(await _changes.GetForEmployeeAsync(employeeProfileId));
}
