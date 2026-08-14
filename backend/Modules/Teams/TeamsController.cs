using AltomateHR.Api.Modules.Teams.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltomateHR.Api.Modules.Teams;

[ApiController]
[Route("teams")]
[Authorize(Roles = "Admin,Owner")]   // org structure is an admin/owner concern
public class TeamsController : ControllerBase
{
    private readonly ITeamService _teams;

    public TeamsController(ITeamService teams) => _teams = teams;

    // GET /teams — every team in the org, each with its roster.
    [HttpGet]
    public async Task<IActionResult> GetAll() => Ok(await _teams.GetAllAsync());

    // GET /teams/chain/{employeeId}?module=LEAVE — preview the derived chain for a module.
    [HttpGet("chain/{employeeId}")]
    public async Task<IActionResult> Chain(string employeeId, [FromQuery] ApprovalModule module = ApprovalModule.CLAIMS) =>
        Ok(await _teams.GetApprovalChainAsync(employeeId, module));

    [HttpPost]
    public async Task<IActionResult> Create(CreateTeamDto dto) => ToResponse(await _teams.CreateAsync(dto));

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, SaveTeamDto dto) =>
        ToResponse(await _teams.UpdateAsync(id, dto));

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id) =>
        await _teams.DeleteAsync(id) ? NoContent() : NotFound();

    // POST /teams/{id}/members — add a member or move them to another layer.
    [HttpPost("{id}/members")]
    public async Task<IActionResult> AddMember(string id, SaveMembershipDto dto) =>
        ToResponse(await _teams.AddOrUpdateMemberAsync(id, dto));

    // DELETE /teams/{id}/members/{employeeId} — remove a member.
    [HttpDelete("{id}/members/{employeeId}")]
    public async Task<IActionResult> RemoveMember(string id, string employeeId) =>
        ToResponse(await _teams.RemoveMemberAsync(id, employeeId));

    private IActionResult ToResponse(TeamSaveResult result)
    {
        if (!result.Ok && result.Error is null) return NotFound();
        return result.Ok ? Ok(result.Team) : BadRequest(new { message = result.Error });
    }
}
