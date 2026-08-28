using AltomateHR.Api.Modules.Employees.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AltomateHR.Api.Modules.ApiKeys;

namespace AltomateHR.Api.Modules.Employees;

[ApiController]
[Route("employees")]
[Authorize(Roles = "Admin,Owner")]   // employee admin is admin/owner only
public class EmployeesController : ControllerBase
{
    private readonly IEmployeeService _employees;
    private readonly IEmployeeProfileService _profiles;

    public EmployeesController(IEmployeeService employees, IEmployeeProfileService profiles)
    {
        _employees = employees;
        _profiles = profiles;
    }

    // GET /employees — everyone in the org, with their role + assigned supervisor.
    [RequireScope("employees:read")]
    [HttpGet]
    public async Task<IActionResult> GetAll() => Ok(await _employees.GetAllAsync());

    // GET /employees/{id}/profile — the full HR/statutory profile for one member.
    [RequireScope("employees:read")]
    [HttpGet("{id}/profile")]
    public async Task<IActionResult> GetProfile(string id)
    {
        var profile = await _profiles.GetAsync(id);
        return profile is null ? NotFound() : Ok(profile);   // null → not a member of this org
    }

    // PUT /employees/{id}/profile — upsert that profile (create on first save).
    [HttpPut("{id}/profile")]
    public async Task<IActionResult> SaveProfile(string id, EmployeeProfileDto dto)
    {
        var saved = await _profiles.SaveAsync(id, dto);
        return saved is null ? NotFound() : Ok(saved);
    }

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
