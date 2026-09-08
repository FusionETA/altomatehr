namespace AltomateHR.Api.Modules.Employees.Entities;

// One entry in EmployeeProfile.PayrollDocumentsJson — never serialized to the
// client directly (see Dtos.EmployeeDocumentDto). `StoredFileName` is the
// generated on-disk name; the client only ever sees `Id`.
public class EmployeeDocumentRecord
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime UploadedAt { get; set; }
    public string StoredFileName { get; set; } = string.Empty;
}
