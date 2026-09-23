using System.Security.Cryptography;

namespace AltomateHR.Api.Modules.Claims;

// Receipts for a claim.
//
// Xero Files when the org has a connection, local disk otherwise — the same
// order as leave attachments and the reference app. A receipt sitting beside
// the bill it justifies is what an accountant reconciling in Xero expects, and
// it stops the bytes being this server's problem to back up.
//
// The fallback is not a nicety: filing a claim has to work for an org that
// never connected Xero, and on the day Xero is down.
public class ClaimReceiptStorage : IClaimReceiptStorage
{
    private const long MaxReceiptBytes = 8 * 1024 * 1024;
    private const string ReceiptRoutePrefix = "/claims/receipts";

    private static readonly Dictionary<string, string> AllowedContentTypes = new()
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp",
        ["image/heic"] = ".heic",
        ["image/heif"] = ".heif",
        ["application/pdf"] = ".pdf",
    };

    // What an accountant sees this grouped under in Xero Files, matching the
    // reference app's folder.
    private const string XeroFolder = "Claims";

    private readonly IWebHostEnvironment _environment;
    private readonly Xero.IXeroFileUploader _xero;

    public ClaimReceiptStorage(IWebHostEnvironment environment, Xero.IXeroFileUploader xero)
    {
        _environment = environment;
        _xero = xero;
    }

    public async Task<ClaimReceiptUploadResult> StoreAsync(ClaimReceiptUpload upload)
    {
        if (upload.Length <= 0)
            throw new ArgumentException("Receipt file is empty.");

        if (upload.Length > MaxReceiptBytes)
            throw new ArgumentException("Receipt file must be 8 MB or smaller.");

        if (!AllowedContentTypes.TryGetValue(upload.ContentType, out var fallbackExtension))
            throw new ArgumentException("Upload a JPG, PNG, WEBP, HEIC, HEIF, or PDF receipt.");

        var extension = GetSafeExtension(upload.FileName, fallbackExtension);

        // Buffered because both destinations need the whole thing: Xero takes a
        // multipart body, and the local fallback has to still be writable after
        // a failed upload has already read the stream. Capped at 8 MB above.
        using var buffer = new MemoryStream();
        await upload.Content.CopyToAsync(buffer);
        var bytes = buffer.ToArray();

        var uploaded = await _xero.TryUploadFileAsync(
            XeroFolder, bytes, Path.GetFileName(upload.FileName), upload.ContentType);

        if (uploaded is not null)
        {
            return new ClaimReceiptUploadResult(
                $"{ReceiptRoutePrefix}/xero/{uploaded.FileId}", uploaded.FileId);
        }

        var fileName = $"{DateTime.UtcNow:yyyyMMddHHmmss}-{RandomNumberGenerator.GetHexString(8).ToLowerInvariant()}{extension}";
        var uploadDirectory = GetUploadDirectory();

        Directory.CreateDirectory(uploadDirectory);

        var path = Path.Combine(uploadDirectory, fileName);
        await File.WriteAllBytesAsync(path, bytes);

        return new ClaimReceiptUploadResult($"{ReceiptRoutePrefix}/{fileName}");
    }

    public Task<ClaimReceiptFileResult?> GetAsync(string fileName)
    {
        var safeFileName = Path.GetFileName(fileName);
        if (!string.Equals(fileName, safeFileName, StringComparison.Ordinal))
            return Task.FromResult<ClaimReceiptFileResult?>(null);

        var extension = Path.GetExtension(safeFileName).ToLowerInvariant();
        var contentType = AllowedContentTypes.FirstOrDefault(pair => pair.Value == extension).Key;
        if (contentType is null)
            return Task.FromResult<ClaimReceiptFileResult?>(null);

        var path = Path.Combine(GetUploadDirectory(), safeFileName);
        if (!File.Exists(path))
            return Task.FromResult<ClaimReceiptFileResult?>(null);

        return Task.FromResult<ClaimReceiptFileResult?>(
            new ClaimReceiptFileResult(path, contentType, safeFileName));
    }

    private static string GetSafeExtension(string fileName, string fallbackExtension)
    {
        var extension = Path.GetExtension(Path.GetFileName(fileName)).ToLowerInvariant();
        return AllowedContentTypes.ContainsValue(extension) ? extension : fallbackExtension;
    }

    private string GetUploadDirectory() =>
        Path.Combine(_environment.ContentRootPath, "storage", "receipts");
}
