using System.ComponentModel.DataAnnotations;

namespace AltomateHR.Api.Modules.Teams.Dtos;

public class TeamMemberDto
{
    public string EmployeeId { get; set; } = string.Empty;
    public string? Email { get; set; }
    public int Layer { get; set; }
}

public class TeamDto
{
    public string Id { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int LayerCount { get; set; }
    public List<string> LayerLabels { get; set; } = [];
    public Dictionary<string, List<int>> ModuleApprovalConfig { get; set; } = new();
    public List<TeamMemberDto> Members { get; set; } = [];
}

public class CreateTeamDto
{
    [Required, MaxLength(40)]
    public string ProjectId { get; set; } = string.Empty;

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [Range(1, 6)]
    public int LayerCount { get; set; } = 1;

    public List<string> LayerLabels { get; set; } = [];
    public Dictionary<string, List<int>> ModuleApprovalConfig { get; set; } = new();
}

public class SaveTeamDto
{
    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [Range(1, 6)]
    public int LayerCount { get; set; } = 1;

    public List<string> LayerLabels { get; set; } = [];
    public Dictionary<string, List<int>> ModuleApprovalConfig { get; set; } = new();
}

public class ChainApproverDto
{
    public string EmployeeId { get; set; } = string.Empty;
    public string? Email { get; set; }
}

public class ApprovalStepDto
{
    public int Step { get; set; }
    public string LayerLabel { get; set; } = string.Empty;
    public List<ChainApproverDto> Approvers { get; set; } = [];
}

// Add a member, or move an existing member to a different layer.
public class SaveMembershipDto
{
    [Required, MaxLength(40)]
    public string EmployeeId { get; set; } = string.Empty;

    [Range(0, 5)]
    public int Layer { get; set; }
}

// Body for PUT .../approvers/{layer}. An empty list is a deliberate "nobody
// approves here for this person" — not the same as never setting it.
public class SetApproverOverrideDto
{
    public List<string> ApproverIds { get; set; } = [];
}

// A candidate for an explicit approver pick: someone sitting at the layer in
// question, minus anyone administrative (never a valid approver anywhere).
public class ApproverCandidateDto
{
    public string EmployeeId { get; set; } = string.Empty;
    public string? Email { get; set; }
}

// One layer above an employee's own, and who currently approves for them at
// it — either the team's implicit default (everyone else at that layer) or
// an explicit override, distinguished by `IsOverridden`.
public class LayerApproverOptionsDto
{
    public int Layer { get; set; }
    public string LayerLabel { get; set; } = string.Empty;
    public List<ApproverCandidateDto> Candidates { get; set; } = [];
    // The approver ids actually in effect right now — the override if one
    // exists, else the same set as Candidates (today's implicit default).
    public List<string> EffectiveApproverIds { get; set; } = [];
    public bool IsOverridden { get; set; }
}

// A team the caller oversees, and the members sitting BELOW them in it.
//
// "Below", not "everyone": a Lead looking at their team means the Staff under
// them, not the Manager above. Teams belong to a project, which is what makes
// this the unit a supervisor switches between — one person can lead a crew on
// two sites and needs to look at them separately.
public class SupervisedTeamDto
{
    public string TeamId { get; set; } = string.Empty;
    public string TeamName { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public List<string> MemberIds { get; set; } = [];
}
