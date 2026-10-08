using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Documents.Entities;

// A letter generated for an employee and kept on file.
//
// ADMIN-ONLY by construction: this table is only ever read through
// DocumentsController, which is [Authorize(Roles = "Admin,Owner")]. It is
// deliberately NOT the employee documents list on EmployeeProfile, which the
// employee can see in their own portal.
//
// The template's name and category are SNAPSHOTS: renaming or deleting the
// template later must not change what the record says was sent. The values the
// letter was rendered with are kept too (ValuesJson), so what was filled in by
// hand for this one letter stays answerable.
public class GeneratedDocument : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;   // tenant

    // The template it came from — informational; it may since have been deleted.
    [MaxLength(40)]
    public string? TemplateId { get; set; }

    [MaxLength(160)]
    public string TemplateName { get; set; } = string.Empty;

    public DocumentCategory Category { get; set; } = DocumentCategory.OTHER;

    // The employee the letter is addressed to (User id).
    [MaxLength(40)]
    public string EmployeeUserId { get; set; } = string.Empty;

    // The admin who generated it, and their name at the time.
    [MaxLength(40)]
    public string? GeneratedByUserId { get; set; }

    [MaxLength(160)]
    public string? GeneratedByName { get; set; }

    // The name the file downloads as.
    [MaxLength(240)]
    public string FileName { get; set; } = string.Empty;

    // The generated on-disk name under storage/generated-documents/{org}/{user}/.
    // Never shown to the client.
    [MaxLength(120)]
    public string StoredFileName { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    // { "employee.name": "…", "input.lastWorkingDay": "…" } — exactly what was
    // printed, after the admin's per-letter overrides.
    public string? ValuesJson { get; set; }

    public DateTime CreatedAt { get; set; }
}
