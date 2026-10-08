using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Modules.Documents.Entities;

namespace AltomateHR.Api.Modules.Documents.Dtos;

// ─── Templates ───────────────────────────────────────────────────────────

public class DocumentTemplateDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DocumentCategory Category { get; set; }
    public string Body { get; set; } = string.Empty;

    // Came from "Add sample templates" — its wording is placeholder text.
    public bool IsSample { get; set; }

    // The merge fields the body uses, registry and input.* alike.
    public List<string> Fields { get; set; } = [];

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class SaveDocumentTemplateDto
{
    [Required, MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    public DocumentCategory Category { get; set; } = DocumentCategory.OTHER;

    [Required, MaxLength(20000)]
    public string Body { get; set; } = string.Empty;
}

// What "Add sample templates" did: how many it created, and how many it
// skipped because the company already has them.
public class AddSampleTemplatesResultDto
{
    public int Added { get; set; }
    public int Skipped { get; set; }
    public List<DocumentTemplateDto> Templates { get; set; } = [];
}

// ─── Merge fields ────────────────────────────────────────────────────────

public class MergeFieldDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public MergeFieldSource Source { get; set; }
    public MergeFieldKind Kind { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool WritableToEmployee { get; set; }
}

// ─── Preview / resolve / generate ────────────────────────────────────────

// Preview a template. With an id, Body (when sent) previews unsaved edits of
// that template; without one, Body is required. EmployeeUserId fills the
// fields from that person's record — without it they print as [Labels].
public class PreviewLetterDto
{
    [MaxLength(160)]
    public string? Name { get; set; }

    [MaxLength(20000)]
    public string? Body { get; set; }

    [MaxLength(40)]
    public string? EmployeeUserId { get; set; }
}

public class ResolveLetterDto
{
    [Required, MaxLength(40)]
    public string EmployeeUserId { get; set; } = string.Empty;
}

// One field a letter needs, filled from the records.
public class ResolvedFieldDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public MergeFieldSource Source { get; set; }
    public MergeFieldKind Kind { get; set; }

    // As it will print ("8 October 2026", "RM 4,500.00"). Null when missing.
    public string? Value { get; set; }

    // The same value in the form an input takes (yyyy-MM-dd, 4500.00), for
    // "edit for this letter". Null when missing.
    public string? EditValue { get; set; }

    // Nothing on file — the admin must fill it in (or explicitly leave it
    // blank) before the letter can be generated. Always true for input.* fields.
    public bool Missing { get; set; }

    // A gap here can be saved back to the employee's record.
    public bool WritableToEmployee { get; set; }

    // Where to fix a gap at its source, e.g. "Payroll → Company Info".
    public string? FixHint { get; set; }
}

public class ResolvedLetterDto
{
    public string TemplateId { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public string EmployeeUserId { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public List<ResolvedFieldDto> Fields { get; set; } = [];
}

public class GenerateLetterDto
{
    [Required, MaxLength(40)]
    public string EmployeeUserId { get; set; } = string.Empty;

    // Per-letter values, keyed by field: what the admin typed for a missing
    // field, an input.* field, or an override of a value on file. Dates as
    // yyyy-MM-dd and money as a number print in letter form.
    public Dictionary<string, string?>? Values { get; set; }

    // Fields the admin chose to leave blank. Any other field without a value
    // refuses the letter.
    public List<string>? LeaveBlank { get; set; }

    // Missing employee fields whose typed value should also be written to the
    // employee's record. Only fields that are empty on the record are written.
    public List<string>? SaveToEmployee { get; set; }

    // Keep a copy on the employee's file (admin-only). Default on.
    public bool Save { get; set; } = true;
}

// ─── Generated letters ───────────────────────────────────────────────────

public class GeneratedDocumentDto
{
    public string Id { get; set; } = string.Empty;
    public string? TemplateId { get; set; }
    public string TemplateName { get; set; } = string.Empty;
    public DocumentCategory Category { get; set; }
    public string EmployeeUserId { get; set; } = string.Empty;
    public string? EmployeeName { get; set; }
    public string? GeneratedByName { get; set; }
    public string FileName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; }
}
