using AltomateHR.Api.Modules.Payroll.Dtos;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

// Saved logins for the statutory portals.
//
// ⚠️ Reading one returns the PASSWORD IN CLEAR. That is the entire feature —
// an admin saves it so they can copy it at filing time — but it means every
// read here is a credential disclosure and must stay Admin/Owner only.
public interface IPortalCredentialService
{
    // Every portal, whether or not a login has been saved for it, so the UI
    // renders one card each. Passwords are MASKED here: the list is a
    // dashboard, and a page that shows every password at once is one
    // shoulder-surf away from losing all of them.
    Task<IReadOnlyList<PortalCredentialDto>> GetAllAsync();

    // One portal, with the password revealed. Deliberately a separate call
    // behind an explicit click, and audited.
    Task<PortalCredentialDto?> RevealAsync(PortalKind portal);

    Task<PortalCredentialDto> SaveAsync(PortalKind portal, SavePortalCredentialDto dto);

    // False when nothing was saved for that portal.
    Task<bool> DeleteAsync(PortalKind portal);
}
