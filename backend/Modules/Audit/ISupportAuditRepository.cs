using AltomateHR.Api.Modules.Audit.Entities;

namespace AltomateHR.Api.Modules.Audit;

public interface ISupportAuditRepository
{
    Task AddAsync(SupportAuditLog entry);
}
