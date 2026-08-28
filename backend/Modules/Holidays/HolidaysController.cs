using AltomateHR.Api.Modules.ApiKeys;
using AltomateHR.Api.Modules.Holidays.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltomateHR.Api.Modules.Holidays;

[ApiController]
[Route("holidays")]
[Authorize]
public class HolidaysController : ControllerBase
{
    private readonly IHolidayService _holidays;

    public HolidaysController(IHolidayService holidays) => _holidays = holidays;

    // GET /holidays — any authenticated user (employees need to see which days
    // are holidays). Pass ?from=&to= to scope to a date range.
    [RequireScope("attendance:read")]
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] DateTime? from, [FromQuery] DateTime? to) =>
        Ok(from is not null && to is not null
            ? await _holidays.GetInRangeAsync(from.Value, to.Value)
            : await _holidays.GetAllAsync());

    [Authorize(Roles = "Admin,Owner")]
    [HttpPost]
    public async Task<IActionResult> Create(SaveHolidayDto dto)
    {
        var result = await _holidays.CreateAsync(dto);
        return result.Ok ? Ok(result.Holiday) : BadRequest(new { message = result.Error });
    }

    [Authorize(Roles = "Admin,Owner")]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, SaveHolidayDto dto)
    {
        var result = await _holidays.UpdateAsync(id, dto);
        if (!result.Ok && result.Error is null) return NotFound();
        return result.Ok ? Ok(result.Holiday) : BadRequest(new { message = result.Error });
    }

    [Authorize(Roles = "Admin,Owner")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id) =>
        await _holidays.DeleteAsync(id) ? NoContent() : NotFound();
}
