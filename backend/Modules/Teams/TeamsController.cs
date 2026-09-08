using AltomateHR.Api.Modules.Approvals;
using AltomateHR.Api.Modules.Teams.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AltomateHR.Api.Modules.ApiKeys;

namespace AltomateHR.Api.Modules.Teams;

[ApiController]
[Route("teams")]
[Authorize(Roles = "Admin,Owner")]   // org structure is an admin/owner concern
public class TeamsController : ControllerBase
{
    private readonly ITeamService _teams;

    private readonly IApprovalReconciliationService _reconciliation;
    private readonly ILogger<TeamsController> _logger;

    public TeamsController(
        ITeamService teams,
        IApprovalReconciliationService reconciliation,
        ILogger<TeamsController> logger)
    {
        _teams = teams;
        _reconciliation = reconciliation;
        _logger = logger;
    }

    // GET /teams — every team in the org, each with its roster.
    [RequireScope("teams:read")]
    [HttpGet]
    public async Task<IActionResult> GetAll() => Ok(await _teams.GetAllAsync());

    // GET /teams/chain/{employeeId}?module=LEAVE — preview the derived chain for a module.
    [RequireScope("teams:read")]
    [HttpGet("chain/{employeeId}")]
    public async Task<IActionResult> Chain(string employeeId, [FromQuery] ApprovalModule module = ApprovalModule.CLAIMS) =>
        Ok(await _teams.GetApprovalChainAsync(employeeId, module));

    [HttpPost]
    public async Task<IActionResult> Create(CreateTeamDto dto) => await ToResponse(await _teams.CreateAsync(dto));

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, SaveTeamDto dto) =>
        await ToResponse(await _teams.UpdateAsync(id, dto), healApprovals: true);

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        if (!await _teams.DeleteAsync(id)) return NotFound();
        await HealStrandedApprovalsAsync();
        return NoContent();
    }

    // POST /teams/{id}/members — add a member or move them to another layer.
    [HttpPost("{id}/members")]
    public async Task<IActionResult> AddMember(string id, SaveMembershipDto dto) =>
        // Moving someone DOWN a layer shortens the chain above them just as
        // removing them would, so this heals too.
        await ToResponse(await _teams.AddOrUpdateMemberAsync(id, dto), healApprovals: true);

    // DELETE /teams/{id}/members/{employeeId} — remove a member.
    [HttpDelete("{id}/members/{employeeId}")]
    public async Task<IActionResult> RemoveMember(string id, string employeeId) =>
        await ToResponse(await _teams.RemoveMemberAsync(id, employeeId), healApprovals: true);

    // Editing the hierarchy can leave in-flight requests parked past the end of
    // a chain that just got shorter — nothing routes to them and no waiting
    // clears them.
    //
    // A request whose step still exists needs no help: the chain is rebuilt
    // dense on every read, so pulling a middle layer out re-indexes the steps
    // and the request simply advances to the next surviving layer. Only one
    // parked BEYOND the last remaining layer is stranded, and that one is
    // finished — every layer that was going to review it is gone.
    //
    // Done here, on the deliberate edit that caused it, rather than as a
    // background sweep: a sweep would also fire mid-edit, when a team briefly
    // has no approvers, and approve everything on a transient org chart.
    private async Task HealStrandedApprovalsAsync()
    {
        try
        {
            await _reconciliation.RunAsync(apply: true);
        }
        catch (Exception ex)
        {
            // The team edit itself succeeded and has been saved. Failing the
            // response now would tell the admin their change didn't land.
            _logger.LogError(ex, "Team edit saved, but healing stranded approvals failed.");
        }
    }

    private async Task<IActionResult> ToResponse(TeamSaveResult result, bool healApprovals = false)
    {
        if (!result.Ok && result.Error is null) return NotFound();
        if (!result.Ok) return BadRequest(new { message = result.Error });
        if (healApprovals) await HealStrandedApprovalsAsync();
        return Ok(result.Team);
    }
}
