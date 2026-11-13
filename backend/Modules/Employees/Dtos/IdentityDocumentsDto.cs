using AltomateHR.Api.Modules.Employees.Entities;

namespace AltomateHR.Api.Modules.Employees.Dtos;

// A person's work permit and passport dates, for the expiry card and the
// expiry reminders. The caller decides who they apply to (ExpiringDocuments):
// a citizen or PR has no permit to renew, and someone archived or gone no
// longer needs one.
public class IdentityDocumentsDto
{
    public string UserId { get; set; } = string.Empty;
    public string? Nationality { get; set; }
    public bool HasPr { get; set; }
    public bool IsArchived { get; set; }
    public DateTime? LeaveDate { get; set; }
    public string? WorkPermitNumber { get; set; }
    public DateTime? WorkPermitExpiry { get; set; }

    // The passport number is the ID number when the ID type is Passport.
    public string? PassportNumber { get; set; }
    public DateTime? PassportExpiry { get; set; }

    public static IdentityDocumentsDto From(EmployeeProfile p) => new()
    {
        UserId = p.UserId,
        Nationality = p.Nationality,
        HasPr = p.HasPr,
        IsArchived = p.IsArchived,
        LeaveDate = p.LeaveDate,
        WorkPermitNumber = p.WorkPermitNumber,
        WorkPermitExpiry = p.WorkPermitExpiry?.Date,
        PassportNumber = p.IdType == IdType.PASSPORT ? p.IdNumber : null,
        PassportExpiry = p.PassportExpiry?.Date,
    };
}
