using System.Security.Cryptography;

namespace AltomateHR.Api.Modules.Overtime;

// Before/after work photos on an overtime request — the evidence an approver
// reviews, and the reason a request cannot be approved without one.
//
// Xero Files when the org has a connection, local disk otherwise: the same
// order as attendance photos, claim receipts and leave attachments. The
// fallback keeps someone able to file overtime when Xero is unreachable.
//
// No new columns. BeforePhotoUrl and AfterPhotoUrl each hold a url, and a
// Xero-hosted photo carries its file id inside that url — so the id is
// recoverable without a second field to keep in step.
public class OvertimePhotoStorage : IOvertimePhotoStorage
{
    private const long MaxPhotoBytes = 8 * 1024 * 1024;
    private const string PhotoRoutePrefix = "/overtime/photos";

    private static readonly Dictionary<string, string> AllowedContentTypes = new()
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp",
        ["image/heic"] = ".heic",
        ["image/heif"] = ".heif",
    };

    // What an accountant sees this grouped under in Xero Files.
    private const string XeroFolder = "Overtime Photos";

    // The url segment marking a photo as Xero-hosted; everything after it is
    // the file id.
    public const string XeroSegment = "xero";

    private readonly IWebHostEnvironment _environment;
    private readonly Xero.IXeroFileUploader _xero;

    public OvertimePhotoStorage(IWebHostEnvironment environment, Xero.IXeroFileUploader xero)
    {
        _environment = environment;
        _xero = xero;
    }

    public async Task<OvertimePhotoUploadResult> StoreAsync(OvertimePhotoUpload upload)
    {
        if (upload.Length <= 0)
            throw new ArgumentException("Photo file is empty.");

        if (upload.Length > MaxPhotoBytes)
            throw new ArgumentException("Photo must be 8 MB or smaller.");

        if (!AllowedContentTypes.TryGetValue(upload.ContentType, out var fallbackExtension))
            throw new ArgumentException("Upload a JPG, PNG, WEBP, HEIC, or HEIF photo.");

        var extension = GetSafeExtension(upload.FileName, fallbackExtension);

        // Buffered because both destinations need the whole thing: Xero takes a
        // multipart body, and the local fallback must still be writable after a
        // failed upload has already read the stream. Capped at 8 MB above.
        using var buffer = new MemoryStream();
        await upload.Content.CopyToAsync(buffer);
        var bytes = buffer.ToArray();

        var uploaded = await _xero.TryUploadFileAsync(
            XeroFolder, bytes, Path.GetFileName(upload.FileName), upload.ContentType);

        if (uploaded is not null)
            return new OvertimePhotoUploadResult($"{PhotoRoutePrefix}/{XeroSegment}/{uploaded.FileId}");

        var fileName = $"{DateTime.UtcNow:yyyyMMddHHmmss}-{RandomNumberGenerator.GetHexString(8).ToLowerInvariant()}{extension}";
        var uploadDirectory = GetUploadDirectory();

        Directory.CreateDirectory(uploadDirectory);

        var path = Path.Combine(uploadDirectory, fileName);
        await File.WriteAllBytesAsync(path, bytes);

        return new OvertimePhotoUploadResult($"{PhotoRoutePrefix}/{fileName}");
    }

    public Task<OvertimePhotoFileResult?> GetAsync(string fileName)
    {
        var safeFileName = Path.GetFileName(fileName);
        if (!string.Equals(fileName, safeFileName, StringComparison.Ordinal))
            return Task.FromResult<OvertimePhotoFileResult?>(null);

        var extension = Path.GetExtension(safeFileName).ToLowerInvariant();
        var contentType = AllowedContentTypes.FirstOrDefault(pair => pair.Value == extension).Key;
        if (contentType is null)
            return Task.FromResult<OvertimePhotoFileResult?>(null);

        var path = Path.Combine(GetUploadDirectory(), safeFileName);
        if (!File.Exists(path))
            return Task.FromResult<OvertimePhotoFileResult?>(null);

        return Task.FromResult<OvertimePhotoFileResult?>(
            new OvertimePhotoFileResult(path, contentType, safeFileName));
    }

    public Task<bool> DeleteAsync(string fileName)
    {
        var safeFileName = Path.GetFileName(fileName);
        if (!string.Equals(fileName, safeFileName, StringComparison.Ordinal))
            return Task.FromResult(false);

        var path = Path.Combine(GetUploadDirectory(), safeFileName);
        try
        {
            if (File.Exists(path)) File.Delete(path);
            return Task.FromResult(true);
        }
        catch (IOException)
        {
            return Task.FromResult(false);
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult(false);
        }
    }

    private static string GetSafeExtension(string fileName, string fallbackExtension)
    {
        var extension = Path.GetExtension(Path.GetFileName(fileName)).ToLowerInvariant();
        return AllowedContentTypes.ContainsValue(extension) ? extension : fallbackExtension;
    }

    private string GetUploadDirectory() =>
        Path.Combine(_environment.ContentRootPath, "storage", "overtime-photos");
}
