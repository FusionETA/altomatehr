namespace AltomateHR.Api.Common;

// Marks an entity as belonging to an Organization (a tenant).
// AppDbContext auto-STAMPS OrganizationId on insert and auto-FILTERS every query by it,
// so no query can accidentally read or write another tenant's rows.
public interface ITenantScoped
{
    string OrganizationId { get; set; }
}
