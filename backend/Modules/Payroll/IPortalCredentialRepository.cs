using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

// One row per (org, portal), so there is no id-based lookup — the tenant
// filter plus the portal IS the key.
public interface IPortalCredentialRepository
{
    Task<List<PayrollPortalCredential>> GetAllAsync();
    Task<PayrollPortalCredential?> GetAsync(PortalKind portal);
    Task<PayrollPortalCredential> AddAsync(PayrollPortalCredential credential);
    Task UpdateAsync(PayrollPortalCredential credential);
    Task DeleteAsync(PayrollPortalCredential credential);
}
