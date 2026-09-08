using AltomateHR.Api.Modules.Employees.Dtos;

namespace AltomateHR.Api.Modules.Employees;

public interface IEmployeeDocumentService
{
    // null → not a member of this org (404).
    Task<IEnumerable<EmployeeDocumentDto>?> GetAllAsync(string userId);

    // (false, null, null) → not a member of this org (404).
    // (false, null, error) → rejected upload (400) — size/type.
    Task<(bool Ok, EmployeeDocumentDto? Document, string? Error)> UploadAsync(
        string userId, EmployeeDocumentUpload upload);

    // false → not a member of this org, or no such document (404).
    Task<bool> DeleteAsync(string userId, string documentId);

    // null → not a member of this org, or no such document (404).
    Task<EmployeeDocumentFileResult?> GetFileAsync(string userId, string documentId);
}
