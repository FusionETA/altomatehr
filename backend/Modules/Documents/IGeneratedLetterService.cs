using AltomateHR.Api.Modules.Documents.Dtos;

namespace AltomateHR.Api.Modules.Documents;

// Turning a template into a letter for one employee, and the letters kept on
// file. Admin-only throughout — nothing here is reachable from the employee
// portal.
public interface IGeneratedLetterService
{
    // Every merge field the template uses (plus the date and signature the
    // PDF always prints), filled from this employee's and the company's
    // records, with each gap flagged. Not found → the template or the
    // employee isn't in this company (or outside a limited admin's scope).
    Task<ResolveResult> ResolveAsync(string templateId, string employeeUserId);

    // A PDF of the template. With a template id, dto.Body (when sent) previews
    // unsaved edits; without one, dto.Body is required. Fields without a value
    // print as [Label].
    Task<LetterFileResult> PreviewAsync(string? templateId, PreviewLetterDto dto);

    // The letter as a PDF. Refused (with the list) while any field has no
    // value and wasn't explicitly left blank. Writes requested gaps back to
    // the employee record, and keeps a copy on file when dto.Save.
    Task<LetterFileResult> GenerateAsync(string templateId, GenerateLetterDto dto);

    // Letters on file, newest first. Null employee = the whole company.
    Task<List<GeneratedDocumentDto>> ListAsync(string? employeeUserId);

    Task<LetterFileResult> DownloadAsync(string id);

    // False → not found.
    Task<bool> DeleteAsync(string id);
}
