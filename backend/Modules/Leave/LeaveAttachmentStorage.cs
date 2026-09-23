using System.Security.Cryptography;

namespace AltomateHR.Api.Modules.Leave;

// Supporting documents for a leave application — an MC, a hospital slip, a
// letter.
//
// Xero Files when the org has a connection, local disk otherwise. That order
// is the reference app's: an accountant reconciling in Xero finds the MC
// attached to the org they already have open, and the bytes stop being this
// server's problem to back up. Applying for leave still has to work for an org
// that never connected Xero — and on the day Xero is down — so local disk
// stays as the fallback rather than the upload failing.
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

    // What an accountant sees this grouped under in Xero Files.
    private const string XeroFolder = "Leave Attachments";

    private readonly IWebHostEnvironment _environment;
    private readonly Xero.IXeroFileUploader _xero;

    public LeaveAttachmentStorage(IWebHostEnvironment environment, Xero.IXeroFileUploader xero)
    {
        _environment = environment;
        _xero = xero;
    }

    public async Task<LeaveAttachmentUploadResult> StoreAsync(LeaveAttachmentUpload upload)
    {
        if (upload.Length <= 0)
            throw new ArgumentException("Attachment is empty.");

        if (upload.Length > MaxAttachmentBytes)
            throw new ArgumentException("Attachment must be 8 MB or smaller.");

        if (!AllowedContentTypes.TryGetValue(upload.ContentType, out var fallbackExtension))
            throw new ArgumentException("Attach a JPG, PNG, WEBP, HEIC, HEIF, or PDF.");

        var extension = GetSafeExtension(upload.FileName, fallbackExtension);

        // Buffered because both destinations need the whole thing: Xero takes a
        // multipart body, and the local fallback has to be writable AFTER a
        // failed upload has already read the stream. These are capped at 8 MB
        // above, so this is bounded.
        using var buffer = new MemoryStream();
        await upload.Content.CopyToAsync(buffer);
        var bytes = buffer.ToArray();

        var uploaded = await _xero.TryUploadFileAsync(
            XeroFolder, bytes, Path.GetFileName(upload.FileName), upload.ContentType);

        if (uploaded is not null)
        {
            // Read back through the API's own proxy, so the OAuth token never
            // reaches the browser.
            return new LeaveAttachmentUploadResult(
                $"/leave/files/{uploaded.FileId}/content", uploaded.FileId);
        }

        var fileName = $"{DateTime.UtcNow:yyyyMMddHHmmss}-{RandomNumberGenerator.GetHexString(8).ToLowerInvariant()}{extension}";
        var uploadDirectory = GetUploadDirectory();

        Directory.CreateDirectory(uploadDirectory);

        var path = Path.Combine(uploadDirectory, fileName);
        await File.WriteAllBytesAsync(path, bytes);

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
