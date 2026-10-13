using AltomateHR.Api.Modules.Organizations.Dtos;

namespace AltomateHR.Api.Modules.Organizations;

// The company's saved signatures for Documents letters (up to 10, at most one
// default). Always the CALLER's company: rows are tenant-filtered, so another
// company's signature id reads as not found everywhere.
public interface IOrganizationSignatureService
{
    // Active signatures, the default first, then by name.
    Task<List<SignatureDto>> ListAsync();

    // Validates the image (type by bytes, ≤ 1 MB, each side ≤ 4000 px,
    // decodable) and the name / position, enforces the 10-signature limit, and
    // stores it. The company's first signature becomes the default.
    Task<SignatureSaveResult> AddAsync(byte[]? content, string? name, string? position, bool isDefault);

    // Renames / moves the default. Not found (404) for a removed signature or
    // one from another company.
    Task<SignatureSaveResult> UpdateAsync(string id, UpdateSignatureDto dto);

    // Soft remove: hidden everywhere, its image file deleted. False → not found.
    Task<bool> RemoveAsync(string id);

    // The image to serve or print. Null when not found, removed, or the file
    // is missing / no longer a readable image.
    Task<SignatureImage?> GetImageAsync(string id);

    // ─── For letters ────────────────────────────────────────────────────

    // An active signature of this company, or null.
    Task<SignatureRef?> FindActiveAsync(string id);

    // The company default, or null when there is none.
    Task<SignatureRef?> GetDefaultAsync();

    // The ids of the active signatures, so a template's default that has
    // since been removed can read as none.
    Task<IReadOnlySet<string>> GetActiveIdsAsync();
}

public sealed record SignatureImage(byte[] Content, string ContentType);

// A signature as a letter uses it: who it names.
public sealed record SignatureRef(string Id, string Name, string Position);

// Ok=false with Error → 400; Ok=false and Error null → not found (404).
public sealed record SignatureSaveResult(bool Ok, SignatureDto? Signature, string? Error)
{
    public static SignatureSaveResult NotFound() => new(false, null, null);

    public static SignatureSaveResult Refused(string error) => new(false, null, error);
}
