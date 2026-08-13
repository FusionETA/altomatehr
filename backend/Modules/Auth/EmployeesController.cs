using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltomateHR.Api.Modules.Auth;

[ApiController]
[Route("employees")]
[Authorize(Roles = "Admin,Owner")]   // employee admin is admin/owner only
public class EmployeesController : ControllerBase
{
    private readonly IEmployeeService _employees;

    public EmployeesController(IEmployeeService employees) => _employees = employees;

    // GET /employees — everyone in the org, with their role + assigned supervisor.
    [HttpGet]
    public async Task<IActionResult> GetAll() => Ok(await _employees.GetAllAsync());

    // PUT /employees/{id} — set a user's role and/or supervisor.
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, UpdateEmployeeDto dto)
    {
        var result = await _employees.UpdateAsync(id, dto);
        if (!result.Ok && result.Error is null) return NotFound();
        return result.Ok ? Ok(result.Employee) : BadRequest(new { message = result.Error });
    }
}
