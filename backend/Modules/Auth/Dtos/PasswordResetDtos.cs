using System.ComponentModel.DataAnnotations;

namespace AltomateHR.Api.Modules.Auth.Dtos;

public class ForgotPasswordDto
{
    [Required, EmailAddress, MaxLength(120)]
    public string Email { get; set; } = string.Empty;
}

public class ResetPasswordDto
{
    [Required, EmailAddress, MaxLength(120)]
    public string Email { get; set; } = string.Empty;

    [Required, RegularExpression(@"^\d{6}$", ErrorMessage = "Enter the 6-digit code from your email.")]
    public string Otp { get; set; } = string.Empty;

    // Minimum only — deliberately not imposing composition rules the rest of the
    // app doesn't (EmployeeService sets passwords with no constraints today).
    [Required, MinLength(8), MaxLength(100)]
    public string NewPassword { get; set; } = string.Empty;
}

// A signed-in user changing their own password.
//
// Distinct from a reset: identity is proved by knowing the CURRENT password
// rather than by possessing the email, so this needs its own endpoint. Nothing
// here identifies the user — that comes from the token, so one account can't
// change another's.
public class ChangePasswordDto
{
    [Required, MaxLength(100)]
    public string CurrentPassword { get; set; } = string.Empty;

    // Same minimum as the reset path, for the same reason: a floor, not
    // composition rules the rest of the app doesn't impose.
    [Required, MinLength(8), MaxLength(100)]
    public string NewPassword { get; set; } = string.Empty;
}
