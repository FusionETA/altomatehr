using System.ComponentModel.DataAnnotations;

namespace AltomateHR.Api.Modules.Employees.Dtos;

// The admin's view of a user/employee (no password, ever).
public class EmployeeDto
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string Role { get; set; } = string.Empty;

    // Per-org employment profile.
    public string? EmployeeNumber { get; set; }
    public string? JobTitle { get; set; }
    public DateTime? JoinDate { get; set; }
    public int OtTimeBalanceMin { get; set; }

    public string? PolicyId { get; set; }
    public string? ShiftId { get; set; }

    // Per-admin module grant. null = full access (no restriction).
    public IReadOnlyList<string>? Modules { get; set; }
}

// What an admin sends to change a user's role and org-membership fields.
public class UpdateEmployeeDto
{
    // null → leave the person's name unchanged. Non-null → update the global User.Name.
    [MaxLength(160)]
    public string? Name { get; set; }

    // null → leave the person's login email unchanged. Non-null → update the
    // global User.Email (rejected if another user already has it).
    [EmailAddress, MaxLength(120)]
    public string? Email { get; set; }

    [MaxLength(40)]
    public string? EmployeeNumber { get; set; }

    [MaxLength(120)]
    public string? JobTitle { get; set; }

    // First day of work IN THIS ORG. Drives pro-rated accrual: setting or
    // correcting it recomputes the employee's earned days for the year.
    public DateTime? JoinDate { get; set; }

    // JoinDate null means "unchanged", so clearing needs saying out loud. Set
    // by the spreadsheet import in "erase blank cells" mode; wins over JoinDate.
    public bool ClearJoinDate { get; set; }

    [Required, MaxLength(20)]
    public string Role { get; set; } = "Employee";

    [MaxLength(40)]
    public string? PolicyId { get; set; }        // null → falls back to the org default policy

    [MaxLength(40)]
    public string? ShiftId { get; set; }         // null → falls back to the project's default shift

    // Per-admin module grant (subset of OrgModules keys). null = full access;
    // [] = locked out. Only meaningful for Admin members.
    public List<string>? Modules { get; set; }
}

// What an admin sends to ADD a member to their org. If the email already belongs
// to a user, that identity is REUSED (this is how the same person ends up in a
// second org); Password is only needed to create a brand-new login account.
public class CreateEmployeeDto
{
    [Required, EmailAddress, MaxLength(120)]
    public string Email { get; set; } = string.Empty;

    // Required only when the email is new (creating a fresh account). Ignored if
    // the account already exists — an existing person keeps their password.
    [MaxLength(100)]
    public string? Password { get; set; }

    // Opt-in, and unticked by default in the dialog. Nothing is sent by the
    // XLSX import, so a 200-row file cannot blast 200 emails by accident.
    public bool SendWelcomeEmail { get; set; }

    // Required when creating a NEW account: the first password is the email
    // followed by this as MMDD (see DefaultPassword). Stored on the profile so
    // the payroll import doesn't ask for the same date again.
    public DateTime? DateOfBirth { get; set; }

    // The person's display name. Required when creating a NEW account; ignored when
    // reusing an existing identity (they keep the name they already have).
    [MaxLength(160)]
    public string? Name { get; set; }

    // Required: the profile flags a missing number as blocking payroll (LHDN's
    // CP39 has a mandatory employee-number column), so the add form must not let
    // an employee in without one. The XLSX import builds this DTO in-process, so
    // this attribute only gates POST /employees — the import keeps its own rules.
    [Required(ErrorMessage = "Enter an employee ID."), MaxLength(40)]
    public string? EmployeeNumber { get; set; }

    [MaxLength(120)]
    public string? JobTitle { get; set; }

    // First day of work IN THIS ORG. Drives pro-rated accrual: setting or
    // correcting it recomputes the employee's earned days for the year.
    public DateTime? JoinDate { get; set; }

    [Required, MaxLength(20)]
    public string Role { get; set; } = "Employee";

    [MaxLength(40)]
    public string? PolicyId { get; set; }

    [MaxLength(40)]
    public string? ShiftId { get; set; }

    // Per-admin module grant. null = full access; [] = locked out.
    public List<string>? Modules { get; set; }
}
