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
