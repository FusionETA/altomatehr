using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Payroll.Entities;

// Which statutory portal a saved login belongs to.
public enum PortalKind
{
    // KWSP i-Akaun — the EPF employer portal.
    KWSP,

    // PERKESO ASSIST — SOCSO and EIS.
    PERKESO,

    // LHDN e-PCB / e-Data PCB.
    LHDN,
}

// The login for one statutory portal, saved so whoever is filing does not
// have to hunt for it every deadline. One row per (org, portal).
//
// The password is encrypted at rest; everything else is plain text, because
// everything else is there to be read. The portals themselves ask for these
// odd extra fields — KWSP shows you a picture you chose at registration,
// PERKESO a security phrase — and an admin who cannot produce them is locked
// out just as surely as one who forgot the password.
public class PayrollPortalCredential : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;   // tenant

    public PortalKind Portal { get; set; }

    [MaxLength(120)]
    public string? LoginId { get; set; }

    // AES-256-GCM, base64. Null until a password is saved. See ISecretBox —
    // this is deliberately reversible, because the point is showing it back.
    public string? PasswordEncrypted { get; set; }

    // KWSP asks for the image you picked at registration, and a secret code
    // it shows at login.
    [MaxLength(120)] public string? Image { get; set; }
    [MaxLength(200)] public string? SecretCode { get; set; }

    // PERKESO's equivalents.
    [MaxLength(200)] public string? SecurityPhrase { get; set; }
    [MaxLength(200)] public string? PasswordReminder { get; set; }

    // Whatever the admin wants to remember — "old PIC was Sarah, rotated
    // 2026-03" is the kind of thing that otherwise lives in someone's head.
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
