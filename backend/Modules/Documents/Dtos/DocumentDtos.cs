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

    // The saved signature its letters default to. Null when none — or when
    // the one chosen has since been removed (the company default applies).
    public string? DefaultSignatureId { get; set; }

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

    // An active saved signature of this company, or null for none (letters
    // then use the company default).
    [MaxLength(40)]
    public string? DefaultSignatureId { get; set; }
}

// What "Add sample templates" did: how many it created, and how many it
// skipped because the company already has them.
public class AddSampleTemplatesResultDto
{
    public int Added { get; set; }
    public int Skipped { get; set; }
    public List<DocumentTemplateDto> Templates { get; set; } = [];
}

// A Word / text file converted to template markup, for the editor to open
// unsaved. Nothing is stored — the editor saves through the create endpoint.
public class TemplateImportDto
{
    // The first heading, else the file name.
    public string SuggestedName { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    // {{…}} placeholders that aren't merge fields or valid {{input.xxx}} —
    // as written (e.g. "Employee Name"), for the editor's "Replace with…" picker.
    public List<string> UnknownFields { get; set; } = [];

    // What didn't come across ("2 images were skipped …").
    public List<string> Warnings { get; set; } = [];
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

    // The saved signature to print: null = the default chain (the template's
    // default → the company default → none); "none" = no signature (sign by
    // hand); otherwise an active signature id of this company.
    [MaxLength(40)]
    public string? SignatureId { get; set; }
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

    // The signature the default chain picks (the template's default → the
    // company default), for the dialog to preselect. Null = none.
    public ResolvedSignatureDto? Signature { get; set; }
}

public class ResolvedSignatureDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
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

    // The saved signature to print: null = the default chain (the template's
    // default → the company default → none); "none" = no signature (sign by
    // hand); otherwise an active signature id of this company.
    [MaxLength(40)]
    public string? SignatureId { get; set; }
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

    // The saved signature printed on it (its name then). Null = none.
    public string? SignatureName { get; set; }
    public string FileName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; }
}

// ─── Write with AI ───────────────────────────────────────────────────────

public class TemplateAiMessageDto
{
    // "user" (the admin) or "assistant" (an earlier reply from the AI).
    [Required]
    [RegularExpression("^(user|assistant)$")]
    public string Role { get; set; } = "user";

    [Required]
    [MaxLength(4000)]
    public string Text { get; set; } = string.Empty;
}

public class TemplateAiRequestDto
{
    // The conversation so far, oldest first; the last one is the admin's request.
    [Required]
    [MinLength(1)]
    [MaxLength(20)]
    public List<TemplateAiMessageDto> Messages { get; set; } = [];

    // Set from the editor's "Ask AI" panel: the draft to revise. Null starts a
    // new template, which comes back as two drafts to choose from.
    [MaxLength(20000)]
    public string? CurrentBody { get; set; }

    [MaxLength(160)]
    public string? CurrentName { get; set; }

    public DocumentCategory? CurrentCategory { get; set; }
}

public class TemplateAiDraftDto
{
    public string Name { get; set; } = string.Empty;
    public DocumentCategory Category { get; set; } = DocumentCategory.OTHER;
    public string Body { get; set; } = string.Empty;

    // {{…}} the AI wrote that aren't merge fields — for the editor's
    // "Replace with…" picker. Saving refuses while any remain.
    public List<string> UnknownFields { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public class TemplateAiResultDto
{
    // What the AI says back to the admin, in their language.
    public string Reply { get; set; } = string.Empty;

    // Two to choose from for a new template; one when revising.
    public List<TemplateAiDraftDto> Drafts { get; set; } = [];
}
