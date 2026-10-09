using AltomateHR.Api.Modules.Organizations.Dtos;

namespace AltomateHR.Api.Modules.Organizations;

// The company logo printed on Documents letters. Always the CALLER's org: the
// controller passes the id from the JWT, the letter service the current
// user's — there is no way to name another company's logo.
public interface IOrganizationLogoService
{
    // Null when the org has no logo, or its file is gone from storage.
    Task<OrganizationLogoFile?> GetAsync(string organizationId);

    // Validates (type by bytes, size, dimensions, decodable), stores, and
    // replaces any previous logo (its file is deleted, best effort).
    Task<LogoSaveResult> SetAsync(string organizationId, byte[]? content);

    // Clears the logo and deletes its file. A no-op when there is none.
    Task RemoveAsync(string organizationId);
}

public sealed record OrganizationLogoFile(byte[] Content, string ContentType);

// Ok=false with Error → 400 (no file, wrong type, too large, unreadable, too
// many pixels); Ok=false and Error null → the org doesn't exist (404).
public sealed record LogoSaveResult(bool Ok, OrganizationDto? Organization, string? Error)
{
    public static LogoSaveResult NotFound() => new(false, null, null);

    public static LogoSaveResult Refused(string error) => new(false, null, error);
}
