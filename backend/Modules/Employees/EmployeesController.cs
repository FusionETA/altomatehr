using AltomateHR.Api.Modules.Employees.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltomateHR.Api.Modules.Employees;

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

    // POST /employees — add a member to THIS org (the admin's active org). If the
    // email already belongs to a user, that identity is reused (second-org case).
    [HttpPost]
    public async Task<IActionResult> Create(CreateEmployeeDto dto)
    {
        var result = await _employees.CreateAsync(dto);
        return result.Ok ? Ok(result.Employee) : BadRequest(new { message = result.Error });
    }

    // PUT /employees/{id} — set a user's role and/or supervisor.
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, UpdateEmployeeDto dto)
    {
        var result = await _employees.UpdateAsync(id, dto);
        if (!result.Ok && result.Error is null) return NotFound();
        return result.Ok ? Ok(result.Employee) : BadRequest(new { message = result.Error });
    }
}
