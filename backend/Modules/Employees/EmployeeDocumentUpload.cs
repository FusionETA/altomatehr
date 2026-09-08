namespace AltomateHR.Api.Modules.Employees;

public sealed record EmployeeDocumentUpload(
    string FileName,
    string ContentType,
    long Length,
    Stream Content);

public sealed record EmployeeDocumentFileResult(
    string Path,
    string ContentType,
    string DownloadName);
