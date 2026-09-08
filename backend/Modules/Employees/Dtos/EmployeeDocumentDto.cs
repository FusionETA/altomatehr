namespace AltomateHR.Api.Modules.Employees.Dtos;

// One document attached to an employee's profile (ID scan, contract,
// certificate, etc). No `Url` — the file is served through an authenticated
// download route keyed by `Id`, not a public path.
public class EmployeeDocumentDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime UploadedAt { get; set; }
}
