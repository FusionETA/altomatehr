using System.ComponentModel.DataAnnotations;

namespace AltomateHR.Api.Modules.Employees.Dtos;

// An admin setting an employee's login password directly.
//
// Minimum 8 matches the forgot-password reset policy, so the two ways into an
// account cannot disagree about what an acceptable password is. Validated here
// AND in the service: this catches the shape, the service owns the rule.
public class SetEmployeePasswordDto
{
    [Required, MinLength(8), MaxLength(100)]
    public string NewPassword { get; set; } = string.Empty;
}
