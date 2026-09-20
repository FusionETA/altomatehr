using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Projects.Entities;

// Who manages a project. A join row, not a column on Project: the legacy system
// models this as a many-to-many (its `ProjectManager` table, unique on
// project+user) and several projects genuinely have more than one manager. The
// scalar `projectManagerId` that also exists over there is called vestigial by
// the legacy schema itself, so it is not reproduced here.
//
// Deliberately NOT part of approval routing. The legacy system let a project's
// manager bypass approval for attendance and OT on that project; v2 does not —
// approval is by hierarchy seat only, and a manager who should skip a step
// belongs at the top layer of the team instead. See ApprovalChainService.
public class ProjectManager : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;   // tenant — auto-stamped + auto-filtered

    [MaxLength(40)]
    public string ProjectId { get; set; } = string.Empty;

    [MaxLength(40)]
    public string UserId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
