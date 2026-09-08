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

    // GET /teams/chain/{employeeId}?module=LEAVE&projectId=... — preview the
    // derived chain for a module; projectId disambiguates when the employee
    // is on more than one team.
    [RequireScope("teams:read")]
    [HttpGet("chain/{employeeId}")]
    public async Task<IActionResult> Chain(
        string employeeId,
        [FromQuery] ApprovalModule module = ApprovalModule.CLAIMS,
        [FromQuery] string? projectId = null) =>
        Ok(await _teams.GetApprovalChainAsync(employeeId, module, projectId));

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

    // GET /teams/{id}/members/{employeeId}/approver-options — per-layer picker
    // data: candidates, what's currently in effect, and whether it's custom.
    [RequireScope("teams:read")]
    [HttpGet("{id}/members/{employeeId}/approver-options")]
    public async Task<IActionResult> GetApproverOptions(string id, string employeeId)
    {
        var options = await _teams.GetApproverOptionsAsync(id, employeeId);
        return options is null ? NotFound() : Ok(options);
    }

    // PUT /teams/{id}/members/{employeeId}/approvers/{layer} — set/replace the
    // explicit approver list for this employee at this layer.
    [HttpPut("{id}/members/{employeeId}/approvers/{layer:int}")]
    public async Task<IActionResult> SetApproverOverride(
        string id, string employeeId, int layer, SetApproverOverrideDto dto) =>
        await ToApproverResponse(
            await _teams.SetApproverOverrideAsync(id, employeeId, layer, dto.ApproverIds));

    // DELETE /teams/{id}/members/{employeeId}/approvers/{layer} — revert to
    // the team's implicit default at this layer.
    [HttpDelete("{id}/members/{employeeId}/approvers/{layer:int}")]
    public async Task<IActionResult> ClearApproverOverride(string id, string employeeId, int layer) =>
        await ToApproverResponse(await _teams.ClearApproverOverrideAsync(id, employeeId, layer));

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

    // An override changes who's asked next, same as adding/moving/removing a
    // member — so it heals stranded approvals too.
    private async Task<IActionResult> ToApproverResponse(ApproverOverrideResult result)
    {
        if (!result.Ok && result.Error is null) return NotFound();
        if (!result.Ok) return BadRequest(new { message = result.Error });
        await HealStrandedApprovalsAsync();
        return Ok(result.Options);
    }
}
