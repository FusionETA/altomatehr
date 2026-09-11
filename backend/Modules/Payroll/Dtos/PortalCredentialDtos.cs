using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll.Dtos;

public class PortalCredentialDto
{
    public PortalKind Portal { get; set; }
    public string PortalLabel { get; set; } = string.Empty;

    public string? LoginId { get; set; }

    // Null on the list, populated only by an explicit reveal. A dashboard
    // showing every password at once is one shoulder-surf from losing them all.
    public string? Password { get; set; }

    // Whether a password is stored at all — the list needs to show "saved"
    // without showing the value.
    public bool HasPassword { get; set; }

    public string? Image { get; set; }
    public string? SecretCode { get; set; }
    public string? SecurityPhrase { get; set; }
    public string? PasswordReminder { get; set; }
    public string? Notes { get; set; }

    // False until anything has been saved for this portal.
    public bool IsConfigured { get; set; }

    public DateTime? UpdatedAt { get; set; }
}

public class SavePortalCredentialDto
{
    [MaxLength(120)] public string? LoginId { get; set; }

    // Omitted leaves the stored password untouched, so an admin can correct a
    // typo in the login id without having to retype the password.
    // Send an empty string to clear it.
    public string? Password { get; set; }

    [MaxLength(120)] public string? Image { get; set; }
    [MaxLength(200)] public string? SecretCode { get; set; }
    [MaxLength(200)] public string? SecurityPhrase { get; set; }
    [MaxLength(200)] public string? PasswordReminder { get; set; }

    [MaxLength(2000)] public string? Notes { get; set; }
}
