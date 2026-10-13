using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Organizations.Dtos;
using AltomateHR.Api.Modules.Organizations.Entities;

namespace AltomateHR.Api.Modules.Organizations;

// Saved signatures: the row (IOrganizationSignatureRepository, tenant-filtered)
// plus the image on disk, in the same branding folder as the company logo
// (IOrganizationLogoStorage). Image checks reuse the logo's: the type comes
// from the bytes (no SVG), the pixel size from the header, and LogoImage.CanDecode
// refuses a truncated or damaged file.
public class OrganizationSignatureService : IOrganizationSignatureService
{
    public const long MaxBytes = 1024 * 1024;
    public const int MaxDimension = 4000;
    public const int MaxSignatures = 10;
    public const int MaxTextLength = 120;

    private readonly IOrganizationSignatureRepository _signatures;
    private readonly IOrganizationLogoStorage _storage;
    private readonly IUserRepository _users;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;
    private readonly ILogger<OrganizationSignatureService>? _logger;

    public OrganizationSignatureService(
        IOrganizationSignatureRepository signatures,
        IOrganizationLogoStorage storage,
        IUserRepository users,
        ICurrentUser currentUser,
        IAuditService audit,
        ILogger<OrganizationSignatureService>? logger = null)
    {
        _signatures = signatures;
        _storage = storage;
        _users = users;
        _currentUser = currentUser;
        _audit = audit;
        _logger = logger;
    }

    public async Task<List<SignatureDto>> ListAsync() =>
        (await _signatures.GetActiveAsync()).Select(ToDto).ToList();

    public async Task<SignatureSaveResult> AddAsync(byte[]? content, string? name, string? position, bool isDefault)
    {
        if (ValidateText(name, position) is { } textError) return SignatureSaveResult.Refused(textError);

        if (content is null || content.Length == 0)
            return SignatureSaveResult.Refused("Choose an image file (PNG, JPG or WebP) for the signature.");
        if (content.LongLength > MaxBytes)
            return SignatureSaveResult.Refused("The signature image is larger than 1 MB. Use a smaller image.");

        var info = LogoImage.Read(content);
        if (info is null)
            return SignatureSaveResult.Refused("The signature must be a PNG, JPG or WebP image.");
        if (info.Width > MaxDimension || info.Height > MaxDimension)
            return SignatureSaveResult.Refused(
                $"The signature image is {info.Width} × {info.Height} pixels. Keep each side to {MaxDimension} pixels or less.");
        if (!LogoImage.CanDecode(content))
            return SignatureSaveResult.Refused("That image can't be read. It may be damaged — try saving it again.");

        var active = await _signatures.GetActiveAsync();
        if (active.Count >= MaxSignatures)
            return SignatureSaveResult.Refused("A company can keep up to 10 signatures. Remove one first.");

        var organizationId = _currentUser.OrganizationId
            ?? throw new InvalidOperationException("No organisation on the request.");

        // The company's first signature is its default; otherwise only when asked.
        var makeDefault = isDefault || active.Count == 0;
        var stored = await _storage.StoreAsync(organizationId, content, info.Extension, kind: "signature");

        var me = _currentUser.UserId is { } uid ? await _users.GetByIdAsync(uid) : null;
        var signature = new OrganizationSignature
        {
            Name = name!.Trim(),
            Position = position!.Trim(),
            StoredFileName = stored,
            ContentType = info.ContentType,
            IsDefault = makeDefault,
            CreatedByUserId = _currentUser.UserId,
            CreatedByName = me?.Name ?? _currentUser.Email,
            CreatedAt = DateTime.UtcNow,
        };

        // A new default takes the flag from the old one, in the same save.
        var previous = makeDefault ? active.Where(s => s.IsDefault).ToArray() : [];
        foreach (var s in previous) s.IsDefault = false;

        try
        {
            await _signatures.AddAsync(signature, previous);
        }
        catch
        {
            _storage.Delete(organizationId, stored);   // don't leave an unreferenced file behind
            throw;
        }

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.SettingsOrgSignatureAdd,
            $"Added the signature \"{signature.Name}\" ({signature.Position})" + (makeDefault ? " as the company default" : ""),
            TargetType: "OrganizationSignature",
            TargetId: signature.Id,
            Metadata: new
            {
                signature.Name,
                signature.Position,
                signature.IsDefault,
                info.ContentType,
                info.Width,
                info.Height,
                SizeBytes = content.LongLength,
            }));

        return new SignatureSaveResult(true, ToDto(signature), null);
    }

    public async Task<SignatureSaveResult> UpdateAsync(string id, UpdateSignatureDto dto)
    {
        var active = await _signatures.GetActiveAsync();
        var signature = active.FirstOrDefault(s => s.Id == id);
        if (signature is null) return SignatureSaveResult.NotFound();
        if (ValidateText(dto.Name, dto.Position) is { } textError) return SignatureSaveResult.Refused(textError);

        var before = new { signature.Name, signature.Position, signature.IsDefault };
        signature.Name = dto.Name.Trim();
        signature.Position = dto.Position.Trim();
        signature.IsDefault = dto.IsDefault;

        // Exactly one default at most: making this one the default clears the rest.
        var changed = new List<OrganizationSignature> { signature };
        if (dto.IsDefault)
        {
            foreach (var other in active.Where(s => s.Id != id && s.IsDefault))
            {
                other.IsDefault = false;
                changed.Add(other);
            }
        }
        await _signatures.UpdateAsync([.. changed]);

        var summary = (before.IsDefault, signature.IsDefault) switch
        {
            (false, true) => $"Set the signature \"{signature.Name}\" as the company default",
            (true, false) => $"Unset the signature \"{signature.Name}\" as the company default",
            _ => $"Edited the signature \"{signature.Name}\"",
        };
        await _audit.WriteAsync(new AuditEvent(
            AuditActions.SettingsOrgSignatureUpdate,
            summary,
            TargetType: "OrganizationSignature",
            TargetId: signature.Id,
            Metadata: new
            {
                Before = before,
                After = new { signature.Name, signature.Position, signature.IsDefault },
            }));

        return new SignatureSaveResult(true, ToDto(signature), null);
    }

    public async Task<bool> RemoveAsync(string id)
    {
        var signature = await _signatures.GetActiveByIdAsync(id);
        if (signature is null) return false;

        var wasDefault = signature.IsDefault;
        signature.RemovedAt = DateTime.UtcNow;
        signature.IsDefault = false;
        await _signatures.UpdateAsync(signature);

        // A removed signature must never print again, so its image goes too;
        // the row stays for the activity log and the letters that name it.
        _storage.Delete(signature.OrganizationId, signature.StoredFileName);

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.SettingsOrgSignatureRemove,
            $"Removed the signature \"{signature.Name}\" ({signature.Position})",
            TargetType: "OrganizationSignature",
            TargetId: signature.Id,
            Metadata: new { signature.Name, signature.Position, WasDefault = wasDefault }));
        return true;
    }

    public async Task<SignatureImage?> GetImageAsync(string id)
    {
        var signature = await _signatures.GetActiveByIdAsync(id);
        if (signature is null) return null;

        var content = await _storage.ReadAsync(signature.OrganizationId, signature.StoredFileName);
        if (content is null)
        {
            _logger?.LogWarning("Signature file {File} for organisation {OrganizationId} is missing from storage.",
                signature.StoredFileName, signature.OrganizationId);
            return null;
        }

        // Re-read the type from the bytes (a file swapped on disk can't be
        // served under a stale type), and refuse one that no longer decodes.
        var info = LogoImage.Read(content);
        if (info is null || !LogoImage.CanDecode(content))
        {
            _logger?.LogWarning("Signature file {File} for organisation {OrganizationId} is not a readable image.",
                signature.StoredFileName, signature.OrganizationId);
            return null;
        }

        return new SignatureImage(content, info.ContentType);
    }

    public async Task<SignatureRef?> FindActiveAsync(string id) =>
        string.IsNullOrWhiteSpace(id) ? null
            : await _signatures.GetActiveByIdAsync(id) is { } s ? new SignatureRef(s.Id, s.Name, s.Position) : null;

    public async Task<SignatureRef?> GetDefaultAsync() =>
        (await _signatures.GetActiveAsync()).FirstOrDefault(s => s.IsDefault) is { } s
            ? new SignatureRef(s.Id, s.Name, s.Position)
            : null;

    public async Task<IReadOnlySet<string>> GetActiveIdsAsync() =>
        (await _signatures.GetActiveAsync()).Select(s => s.Id).ToHashSet(StringComparer.Ordinal);

    private static string? ValidateText(string? name, string? position)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Enter the signatory's name.";
        if (string.IsNullOrWhiteSpace(position)) return "Enter the signatory's position.";
        if (name.Trim().Length > MaxTextLength) return $"Keep the name to {MaxTextLength} characters or less.";
        if (position.Trim().Length > MaxTextLength) return $"Keep the position to {MaxTextLength} characters or less.";
        return null;
    }

    private static SignatureDto ToDto(OrganizationSignature s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        Position = s.Position,
        IsDefault = s.IsDefault,
        // Stored as UTC; MySQL hands it back unspecified.
        CreatedAt = DateTime.SpecifyKind(s.CreatedAt, DateTimeKind.Utc),
        CreatedByName = s.CreatedByName,
    };
}
