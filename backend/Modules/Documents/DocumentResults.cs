using AltomateHR.Api.Modules.Documents.Dtos;

namespace AltomateHR.Api.Modules.Documents;

// Same convention as the rest of the API: Ok=false with Error → 400;
// Ok=false and Error null → not found in this company (404).

public sealed record TemplateSaveResult(
    bool Ok,
    DocumentTemplateDto? Template,
    string? Error,
    // Merge fields the body uses that don't exist — listed so the editor can
    // point at each one.
    IReadOnlyList<string>? UnknownFields = null)
{
    public static TemplateSaveResult NotFound() => new(false, null, null);
}

// Forbidden → 403 with Error: the caller's access to the EMPLOYEE record
// (grant or API key scope) doesn't cover what the letter reads or writes.
public sealed record ResolveResult(bool Ok, ResolvedLetterDto? Letter, string? Error, bool Forbidden = false)
{
    public static ResolveResult NotFound() => new(false, null, null);

    public static ResolveResult Denied(string error) => new(false, null, error, Forbidden: true);
}

// A file for the controller to return with File(...), as StatutoryFileResult.
public sealed record LetterFileResult(
    bool Ok,
    string? FileName,
    byte[]? Content,
    string? ContentType,
    string? Error,
    // The fields still without a value when a letter was refused for gaps.
    IReadOnlyList<string>? MissingFields = null,
    // 403, as ResolveResult.Forbidden.
    bool Forbidden = false)
{
    public const string Pdf = "application/pdf";

    public static LetterFileResult Denied(string error) =>
        new(false, null, null, null, error, Forbidden: true);

    public static LetterFileResult NotFound() => new(false, null, null, null, null);

    public static LetterFileResult Refused(string error, IReadOnlyList<string>? missing = null) =>
        new(false, null, null, null, error, missing);

    public static LetterFileResult File(string fileName, byte[] content) =>
        new(true, fileName, content, Pdf, null);
}
