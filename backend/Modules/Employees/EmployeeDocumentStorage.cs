using System.Security.Cryptography;

namespace AltomateHR.Api.Modules.Employees;

public class EmployeeDocumentStorage : IEmployeeDocumentStorage
{
    private const long MaxDocumentBytes = 10 * 1024 * 1024;

    private static readonly Dictionary<string, string> AllowedContentTypes = new()
    {
        ["application/pdf"] = ".pdf",
        ["application/msword"] = ".doc",
        ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = ".docx",
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp",
        ["image/heic"] = ".heic",
        ["image/heif"] = ".heif",
    };

    private readonly IWebHostEnvironment _environment;

    public EmployeeDocumentStorage(IWebHostEnvironment environment) => _environment = environment;

    public async Task<string> StoreAsync(string userId, EmployeeDocumentUpload upload)
    {
        if (upload.Length <= 0)
            throw new ArgumentException("File is empty.");

        if (upload.Length > MaxDocumentBytes)
            throw new ArgumentException("File must be 10 MB or smaller.");

        if (!AllowedContentTypes.TryGetValue(upload.ContentType, out var fallbackExtension))
            throw new ArgumentException("Upload a PDF, Word document, JPG, PNG, WEBP, HEIC, or HEIF file.");

        var extension = GetSafeExtension(upload.FileName, fallbackExtension);
        var storedFileName =
            $"{DateTime.UtcNow:yyyyMMddHHmmss}-{RandomNumberGenerator.GetHexString(8).ToLowerInvariant()}{extension}";
        var uploadDirectory = GetUploadDirectory(userId);

        Directory.CreateDirectory(uploadDirectory);

        var path = Path.Combine(uploadDirectory, storedFileName);
        await using var output = File.Create(path);
        await upload.Content.CopyToAsync(output);

        return storedFileName;
    }

    public Task<EmployeeDocumentFileResult?> GetAsync(string userId, string storedFileName)
    {
        var safeFileName = Path.GetFileName(storedFileName);
        if (!string.Equals(storedFileName, safeFileName, StringComparison.Ordinal))
            return Task.FromResult<EmployeeDocumentFileResult?>(null);

        var extension = Path.GetExtension(safeFileName).ToLowerInvariant();
        var contentType = AllowedContentTypes.FirstOrDefault(pair => pair.Value == extension).Key;
        if (contentType is null)
            return Task.FromResult<EmployeeDocumentFileResult?>(null);

        var path = Path.Combine(GetUploadDirectory(userId), safeFileName);
        if (!File.Exists(path))
            return Task.FromResult<EmployeeDocumentFileResult?>(null);

        // DownloadName is overwritten by the caller (EmployeeDocumentService)
        // with the admin-facing original name — this stored name is only ever
        // meant for disk lookup.
        return Task.FromResult<EmployeeDocumentFileResult?>(
            new EmployeeDocumentFileResult(path, contentType, safeFileName));
    }

    private static string GetSafeExtension(string fileName, string fallbackExtension)
    {
        var extension = Path.GetExtension(Path.GetFileName(fileName)).ToLowerInvariant();
        return AllowedContentTypes.ContainsValue(extension) ? extension : fallbackExtension;
    }

    private string GetUploadDirectory(string userId) =>
        Path.Combine(_environment.ContentRootPath, "storage", "employee-documents", userId);
}
