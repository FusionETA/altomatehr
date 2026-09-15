using System.Security.Cryptography;

namespace AltomateHR.Api.Modules.Leave;

// Supporting documents for a leave application — an MC, a hospital slip, a
// letter. Stored on disk beside the claim receipts rather than in Xero Files:
// applying for leave must work for an org that has never connected Xero, and
// the previous system falls back to a local path for exactly that reason.
// (LeaveApplication.XeroFileId remains for the Xero-hosted case.)
public class LeaveAttachmentStorage : ILeaveAttachmentStorage
{
    private const long MaxAttachmentBytes = 8 * 1024 * 1024;
    private const string RoutePrefix = "/leave/attachments";

    // An MC is usually a photo or a scan. Office documents are deliberately
    // absent: they carry macros, and nothing here needs to open one.
    private static readonly Dictionary<string, string> AllowedContentTypes = new()
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp",
        ["image/heic"] = ".heic",
        ["image/heif"] = ".heif",
        ["application/pdf"] = ".pdf",
    };

    private readonly IWebHostEnvironment _environment;

    public LeaveAttachmentStorage(IWebHostEnvironment environment) => _environment = environment;

    public async Task<LeaveAttachmentUploadResult> StoreAsync(LeaveAttachmentUpload upload)
    {
        if (upload.Length <= 0)
            throw new ArgumentException("Attachment is empty.");

        if (upload.Length > MaxAttachmentBytes)
            throw new ArgumentException("Attachment must be 8 MB or smaller.");

        if (!AllowedContentTypes.TryGetValue(upload.ContentType, out var fallbackExtension))
            throw new ArgumentException("Attach a JPG, PNG, WEBP, HEIC, HEIF, or PDF.");

        var extension = GetSafeExtension(upload.FileName, fallbackExtension);
        var fileName = $"{DateTime.UtcNow:yyyyMMddHHmmss}-{RandomNumberGenerator.GetHexString(8).ToLowerInvariant()}{extension}";
        var uploadDirectory = GetUploadDirectory();

        Directory.CreateDirectory(uploadDirectory);

        var path = Path.Combine(uploadDirectory, fileName);
        await using var output = File.Create(path);
        await upload.Content.CopyToAsync(output);

        return new LeaveAttachmentUploadResult($"{RoutePrefix}/{fileName}");
    }

    public Task<LeaveAttachmentFileResult?> GetAsync(string fileName)
    {
        // Path.GetFileName strips any directory part, so "../../appsettings.json"
        // can't escape the upload folder. Comparing back catches the attempt
        // rather than silently serving something else.
        var safeFileName = Path.GetFileName(fileName);
        if (!string.Equals(fileName, safeFileName, StringComparison.Ordinal))
            return Task.FromResult<LeaveAttachmentFileResult?>(null);

        var extension = Path.GetExtension(safeFileName).ToLowerInvariant();
        var contentType = AllowedContentTypes.FirstOrDefault(pair => pair.Value == extension).Key;
        if (contentType is null)
            return Task.FromResult<LeaveAttachmentFileResult?>(null);

        var path = Path.Combine(GetUploadDirectory(), safeFileName);
        if (!File.Exists(path))
            return Task.FromResult<LeaveAttachmentFileResult?>(null);

        return Task.FromResult<LeaveAttachmentFileResult?>(
            new LeaveAttachmentFileResult(path, contentType, safeFileName));
    }

    private static string GetSafeExtension(string fileName, string fallbackExtension)
    {
        var extension = Path.GetExtension(Path.GetFileName(fileName)).ToLowerInvariant();
        return AllowedContentTypes.ContainsValue(extension) ? extension : fallbackExtension;
    }

    private string GetUploadDirectory() =>
        Path.Combine(_environment.ContentRootPath, "storage", "leave-attachments");
}
