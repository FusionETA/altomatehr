namespace AltomateHR.Api.Modules.Employees;

public interface IEmployeeDocumentStorage
{
    /// Stores the upload on disk and returns the generated (safe, unique) file
    /// name. Callers persist this in EmployeeDocumentRecord.StoredFileName —
    /// never the admin's original file name, which isn't guaranteed to be
    /// filesystem-safe or unique.
    Task<string> StoreAsync(string userId, EmployeeDocumentUpload upload);

    Task<EmployeeDocumentFileResult?> GetAsync(string userId, string storedFileName);
}
