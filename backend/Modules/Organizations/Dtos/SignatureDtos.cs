using System.ComponentModel.DataAnnotations;

namespace AltomateHR.Api.Modules.Organizations.Dtos;

// A saved signature as Settings and the Documents dialogs see it. The image
// itself is served separately (GET /organizations/current/signatures/{id}/image).
public class SignatureDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public DateTime CreatedAt { get; set; }   // UTC
    public string? CreatedByName { get; set; }
}

// PUT /organizations/current/signatures/{id}. isDefault=true makes it the
// company default (clearing the previous one); false on the current default
// leaves the company with none.
public class UpdateSignatureDto
{
    [Required, MaxLength(OrganizationSignatureService.MaxTextLength)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(OrganizationSignatureService.MaxTextLength)]
    public string Position { get; set; } = string.Empty;

    public bool IsDefault { get; set; }
}
