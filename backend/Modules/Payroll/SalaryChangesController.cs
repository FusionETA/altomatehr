using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltomateHR.Api.Modules.Payroll;

// One employee's salary history.
//
// Read-only here: a change is RECORDED as a side effect of editing the
// employee's salary, so the two can never disagree. A history that could be
// written independently of the profile would be a second source of truth for
// what someone earns.
[ApiController]
[Route("payroll/salary-changes")]
[Authorize(Roles = "Admin,Owner")]
public class SalaryChangesController : ControllerBase
{
    private readonly ISalaryChangeService _changes;

    public SalaryChangesController(ISalaryChangeService changes) => _changes = changes;

    [HttpGet("{employeeProfileId}")]
    public async Task<IActionResult> GetForEmployee(string employeeProfileId) =>
        Ok(await _changes.GetForEmployeeAsync(employeeProfileId));
}
