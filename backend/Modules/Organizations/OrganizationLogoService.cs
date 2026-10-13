using AltomateHR.Api.Modules.Audit;

namespace AltomateHR.Api.Modules.Organizations;

// Company logo: the file on disk (IOrganizationLogoStorage) plus three columns
// on the Organization row (IOrganizationRepository). Organization is not
// tenant-filtered, so the org id handed in is always the caller's own — taken
// from the JWT by the controller, from ICurrentUser by the letter service.
public class OrganizationLogoService : IOrganizationLogoService
{
    public const long MaxBytes = 2 * 1024 * 1024;
    public const int MaxDimension = 4000;

    private readonly IOrganizationRepository _organizations;
    private readonly IOrganizationLogoStorage _storage;
    private readonly IAuditService _audit;
    private readonly ILogger<OrganizationLogoService>? _logger;

    public OrganizationLogoService(
        IOrganizationRepository organizations,
        IOrganizationLogoStorage storage,
        IAuditService audit,
        ILogger<OrganizationLogoService>? logger = null)
    {
        _organizations = organizations;
        _storage = storage;
        _audit = audit;
        _logger = logger;
    }

    public async Task<OrganizationLogoFile?> GetAsync(string organizationId)
    {
        var org = await _organizations.GetByIdAsync(organizationId);
        if (org?.LogoFileName is not { Length: > 0 } stored) return null;

        var content = await _storage.ReadAsync(org.Id, stored);
        if (content is null)
        {
            _logger?.LogWarning("Logo file {File} for organisation {OrganizationId} is missing from storage.", stored, org.Id);
            return null;
        }

        // The type was checked from the bytes on upload; re-read it so a file
        // swapped on disk can't be served under a stale content type.
        var info = LogoImage.Read(content);
        if (info is null)
        {
            _logger?.LogWarning("Logo file {File} for organisation {OrganizationId} is not a readable image.", stored, org.Id);
            return null;
        }

        return new OrganizationLogoFile(content, info.ContentType);
    }

    public async Task<LogoSaveResult> SetAsync(string organizationId, byte[]? content)
    {
        if (content is null || content.Length == 0)
            return LogoSaveResult.Refused("Choose an image file (PNG, JPG or WebP) to upload.");
        if (content.LongLength > MaxBytes)
            return LogoSaveResult.Refused("The logo is larger than 2 MB. Use a smaller image.");

        var info = LogoImage.Read(content);
        if (info is null)
            return LogoSaveResult.Refused("The logo must be a PNG, JPG or WebP image.");
        if (info.Width > MaxDimension || info.Height > MaxDimension)
            return LogoSaveResult.Refused(
                $"The logo is {info.Width} × {info.Height} pixels. Keep each side to {MaxDimension} pixels or less.");
        if (!LogoImage.CanDecode(content))
            return LogoSaveResult.Refused("That image can't be read. It may be damaged — try saving it again.");

        var org = await _organizations.GetByIdAsync(organizationId);
        if (org is null) return LogoSaveResult.NotFound();

        var previous = org.LogoFileName;
        var stored = await _storage.StoreAsync(org.Id, content, info.Extension);

        org.LogoFileName = stored;
        org.LogoContentType = info.ContentType;
        org.LogoUpdatedAt = DateTime.UtcNow;
        try
        {
            await _organizations.UpdateAsync(org);
        }
        catch
        {
            _storage.Delete(org.Id, stored);   // don't leave an unreferenced file behind
            throw;
        }

        if (!string.IsNullOrEmpty(previous) && previous != stored)
            _storage.Delete(org.Id, previous);

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.SettingsOrgLogoUpdate,
            previous is null ? "Uploaded the company logo" : "Replaced the company logo",
            TargetType: "Organization",
            TargetId: org.Id,
            Metadata: new { info.ContentType, info.Width, info.Height, SizeBytes = content.LongLength }));

        return new LogoSaveResult(true, OrganizationService.ToDto(org), null);
    }

    public async Task RemoveAsync(string organizationId)
    {
        var org = await _organizations.GetByIdAsync(organizationId);
        if (org is null || string.IsNullOrEmpty(org.LogoFileName)) return;

        var previous = org.LogoFileName;
        org.LogoFileName = null;
        org.LogoContentType = null;
        org.LogoUpdatedAt = DateTime.UtcNow;
        await _organizations.UpdateAsync(org);
        _storage.Delete(org.Id, previous);

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.SettingsOrgLogoRemove,
            "Removed the company logo",
            TargetType: "Organization",
            TargetId: org.Id));
    }
}
