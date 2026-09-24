using AltomateHR.Api.Modules.ApiKeys;
using AltomateHR.Api.Modules.ApiKeys.Entities;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Organizations.Entities;
using AltomateHR.Api.Data;

namespace AltomateHR.Api.Modules.Provisioning;

// Standing up a new company from outside, for a platform that sells AltomateHR
// alongside its own product.
//
// The bootstrap problem this solves: every other credential here is org-scoped,
// and creating a company cannot require a credential that already belongs to
// one. So this is the only surface a master key opens, and it does exactly two
// things — make the org, and hand back a wp_live_ key scoped to it. Everything
// after that runs on the org key like any other integration.
public interface IProvisioningService
{
    // Null when the master token is unknown or revoked.
    Task<MasterKey?> AuthenticateAsync(string? bearerToken);

    Task<ProvisionedOrganization> CreateOrganizationAsync(string name, IReadOnlyList<string> scopes);
}

// ApiKey is the ONE time the raw token exists. It is returned here and never
// stored, exactly as a customer-created key behaves.
public record ProvisionedOrganization(string OrganizationId, string Name, string ApiKey, string ApiKeyPrefix);

public class ProvisioningService : IProvisioningService
{
    private readonly IMasterKeyRepository _masterKeys;
    private readonly IOrganizationRepository _organizations;
    private readonly AppDbContext _db;

    public ProvisioningService(
        IMasterKeyRepository masterKeys, IOrganizationRepository organizations, AppDbContext db)
    {
        _masterKeys = masterKeys;
        _organizations = organizations;
        _db = db;
    }

    public async Task<MasterKey?> AuthenticateAsync(string? bearerToken)
    {
        var token = bearerToken?.Trim();
        if (string.IsNullOrEmpty(token) || !token.StartsWith(MasterTokenGenerator.Prefix, StringComparison.Ordinal))
            return null;

        var key = await _masterKeys.GetByHashAsync(MasterTokenGenerator.HashToken(token));
        if (key is null || !key.Active) return null;

        // Answers "is this still in use?" before anyone revokes one.
        key.LastUsedAt = DateTime.UtcNow;
        await _masterKeys.UpdateAsync(key);
        return key;
    }

    public async Task<ProvisionedOrganization> CreateOrganizationAsync(
        string name, IReadOnlyList<string> scopes)
    {
        var org = new Organization
        {
            Name = name.Trim(),
            CreatedAt = DateTime.UtcNow,
            // Same starting plan a self-serve company gets, so a provisioned org
            // is not a second kind of tenant with its own quirks.
            Plan = OrgPlan.DIY,
            Tier = OrgPlanTier.PAID,
            Addons = "expense_claim,clock",
        };
        await _organizations.AddAsync(org);

        var (raw, hash, prefix) = ApiTokenGenerator.Generate();

        // Written through the context directly, with OrganizationId set BY HAND.
        // ApiKey is ITenantScoped and its stamp fills a blank org from the
        // CALLER's active org — and a master key has none, so the stamp has
        // nothing to copy. This is the one place that has to say it explicitly.
        _db.ApiKeys.Add(new ApiKey
        {
            OrganizationId = org.Id,
            Name = $"{org.Name} integration",
            TokenHash = hash,
            TokenPrefix = prefix,
            Scopes = ApiScopes.Join(scopes),
            Active = true,
            CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        return new ProvisionedOrganization(org.Id, org.Name, raw, prefix);
    }
}
