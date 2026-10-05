namespace AltomateHR.Api.Modules.Employees.Dtos;

// A work permit on file, for the expiry alert. The caller decides who it applies
// to: a citizen or PR has no permit to renew, and someone archived or gone no
// longer needs one.
public class WorkPermitDto
{
    public string UserId { get; set; } = string.Empty;
    public string? Nationality { get; set; }
    public bool HasPr { get; set; }
    public bool IsArchived { get; set; }
    public DateTime? LeaveDate { get; set; }
    public string? WorkPermitNumber { get; set; }
    public DateTime WorkPermitExpiry { get; set; }
}
