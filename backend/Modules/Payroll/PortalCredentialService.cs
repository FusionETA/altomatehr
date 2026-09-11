using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Payroll.Dtos;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

public class PortalCredentialService : IPortalCredentialService
{
    private readonly IPortalCredentialRepository _credentials;
    private readonly ISecretBox _secrets;
    private readonly IAuditService _audit;

    public PortalCredentialService(
        IPortalCredentialRepository credentials, ISecretBox secrets, IAuditService audit)
    {
        _credentials = credentials;
        _secrets = secrets;
        _audit = audit;
    }

    // Every portal, configured or not, so the UI renders one card each.
    // Passwords are MASKED: this is a dashboard, and rendering every stored
    // password at once is one shoulder-surf away from losing all of them.
    public async Task<IReadOnlyList<PortalCredentialDto>> GetAllAsync()
    {
        var saved = (await _credentials.GetAllAsync())
            .ToDictionary(c => c.Portal, c => c);

        return
        [
            .. Enum.GetValues<PortalKind>().Select(portal =>
                saved.TryGetValue(portal, out var credential)
                    ? ToDto(credential, revealPassword: false)
                    : Empty(portal)),
        ];
    }

    // The password in clear. A separate call behind an explicit click, and
    // audited — reading a credential is an event worth being able to ask
    // about later.
    public async Task<PortalCredentialDto?> RevealAsync(PortalKind portal)
    {
        var credential = await _credentials.GetAsync(portal);
        if (credential is null) return null;

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.PayrollPortalCredentialReveal,
            $"Viewed the saved {Label(portal)} portal password",
            TargetType: "PayrollPortalCredential",
            TargetId: credential.Id,
            Metadata: new { Portal = portal.ToString() }));

        return ToDto(credential, revealPassword: true);
    }

    public async Task<PortalCredentialDto> SaveAsync(
        PortalKind portal, SavePortalCredentialDto dto)
    {
        var credential = await _credentials.GetAsync(portal);
        var isFirstSave = credential is null;
        var now = DateTime.UtcNow;

        credential ??= new PayrollPortalCredential { Portal = portal, CreatedAt = now };

        credential.LoginId = dto.LoginId;
        credential.Image = dto.Image;
        credential.SecretCode = dto.SecretCode;
        credential.SecurityPhrase = dto.SecurityPhrase;
        credential.PasswordReminder = dto.PasswordReminder;
        credential.Notes = dto.Notes;
        credential.UpdatedAt = now;

        // Three distinct meanings, and conflating them is how someone's
        // password quietly disappears:
        //   null          → leave it alone (correcting the login id)
        //   empty string  → clear it
        //   anything else → replace it
        if (dto.Password is not null)
        {
            credential.PasswordEncrypted = dto.Password.Length == 0
                ? null
                : _secrets.Encrypt(dto.Password);
        }

        if (isFirstSave) await _credentials.AddAsync(credential);
        else await _credentials.UpdateAsync(credential);

        // The VALUES are deliberately not in the metadata — an audit log that
        // records the password defeats encrypting it.
        await _audit.WriteAsync(new AuditEvent(
            AuditActions.PayrollPortalCredentialSave,
            $"{(isFirstSave ? "Saved" : "Updated")} the {Label(portal)} portal login",
            TargetType: "PayrollPortalCredential",
            TargetId: credential.Id,
            Metadata: new
            {
                Portal = portal.ToString(),
                PasswordChanged = dto.Password is not null,
            }));

        return ToDto(credential, revealPassword: false);
    }

    public async Task<bool> DeleteAsync(PortalKind portal)
    {
        var credential = await _credentials.GetAsync(portal);
        if (credential is null) return false;

        await _credentials.DeleteAsync(credential);

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.PayrollPortalCredentialDelete,
            $"Deleted the saved {Label(portal)} portal login",
            TargetType: "PayrollPortalCredential",
            TargetId: credential.Id,
            Metadata: new { Portal = portal.ToString() }));

        return true;
    }

    private static PortalCredentialDto Empty(PortalKind portal) => new()
    {
        Portal = portal,
        PortalLabel = Label(portal),
        IsConfigured = false,
    };

    private PortalCredentialDto ToDto(PayrollPortalCredential c, bool revealPassword) => new()
    {
        Portal = c.Portal,
        PortalLabel = Label(c.Portal),
        LoginId = c.LoginId,
        // A blob that will not decrypt — written under a previous key, say —
        // reads as no password rather than taking the page down.
        Password = revealPassword ? _secrets.Decrypt(c.PasswordEncrypted) : null,
        HasPassword = !string.IsNullOrWhiteSpace(c.PasswordEncrypted),
        Image = c.Image,
        SecretCode = c.SecretCode,
        SecurityPhrase = c.SecurityPhrase,
        PasswordReminder = c.PasswordReminder,
        Notes = c.Notes,
        IsConfigured = true,
        UpdatedAt = c.UpdatedAt,
    };

    public static string Label(PortalKind portal) => portal switch
    {
        PortalKind.KWSP => "KWSP i-Akaun",
        PortalKind.PERKESO => "PERKESO ASSIST",
        PortalKind.LHDN => "LHDN e-PCB",
        _ => portal.ToString(),
    };
}
