using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Organizations.Entities;

// A saved signature a company can print on its Documents letters: an image
// plus the name and position printed under it.
//
// Tenant-scoped (global query filter). Removing one is a SOFT remove
// (RemovedAt) so the activity log and the letters on file that name it stay
// readable — but its image file is deleted, so it can never print again.
public class OrganizationSignature : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;   // tenant

    // The signatory's name and position, printed under the image.
    [MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(120)]
    public string Position { get; set; } = string.Empty;

    // The generated on-disk name under storage/org-branding/{org}/. Never
    // shown to the client.
    [MaxLength(120)]
    public string StoredFileName { get; set; } = string.Empty;

    // Read from the file's bytes on upload (image/png, image/jpeg, image/webp).
    [MaxLength(40)]
    public string ContentType { get; set; } = string.Empty;

    // The company default: at most one active signature has it.
    public bool IsDefault { get; set; }

    [MaxLength(40)]
    public string? CreatedByUserId { get; set; }

    // Who added it, as their name read at the time.
    [MaxLength(160)]
    public string? CreatedByName { get; set; }

    public DateTime CreatedAt { get; set; }

    // Set when removed. Removed signatures are never listed, served or printed.
    public DateTime? RemovedAt { get; set; }
}
