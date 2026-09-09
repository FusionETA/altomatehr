using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Overtime.Entities;

public class OvertimeRequest : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;

    [MaxLength(40)]
    public string EmployeeId { get; set; } = string.Empty;

    [MaxLength(40)]
    public string? ProjectId { get; set; }

    public DateTime WorkDate { get; set; }
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
    public int RequestedMinutes { get; set; }

    [MaxLength(1000)]
    public string Reason { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string BeforePhotoUrl { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? AfterPhotoUrl { get; set; }

    public OvertimeStatus Status { get; set; } = OvertimeStatus.PENDING;

    public int CurrentStep { get; set; }

    [MaxLength(1000)]
    public string? ReviewNotes { get; set; }

    // Who last decided on this request. Null where nobody did: a submission
    // auto-approved because the employee has no approver, or one resolved by
    // the unreachable-approval sweep. Naming a person for those would be a
    // fabrication on an audit surface.
    [MaxLength(40)]
    public string? ReviewerId { get; set; }

    public DateTime SubmittedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
