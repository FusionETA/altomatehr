using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.ApiKeys.Entities;

namespace AltomateHR.Api.Modules.ApiKeys;

public class ActingKeyService : IActingKeyService
{
    // A ceiling, not the usual life: FAH ends each key when its turn finishes.
    // Long enough for the slowest turn, short enough that a leaked key is soon useless.
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    private readonly IApiKeyRepository _keys;
    private readonly ICurrentUser _currentUser;

    public ActingKeyService(IApiKeyRepository keys, ICurrentUser currentUser)
    {
        _keys = keys;
        _currentUser = currentUser;
    }

    public async Task<ActingKey> IssueAsync()
    {
        // A person signed in to the app, never a key minting another key.
        if (!_currentUser.IsAuthenticated || _currentUser.IsScopedMachine
            || _currentUser.UserId is not { } userId || _currentUser.OrganizationId is not { } orgId)
            throw new ActingKeyException("Sign in to use FAH.");

        // Support acts as "System (Support)" without a membership; a key would
        // put the staff member's own name on the company's records instead.
        if (_currentUser.IsSupport)
            throw new ActingKeyException("FAH isn't available while you're in a company as support.");
        if (_currentUser.IsFormer)
            throw new ActingKeyException("FAH isn't available on a former employee's account.");

        var member = await _keys.GetActingMemberAsync(orgId, userId);
        if (member is null || !OrgRoles.IsAdministrative(member.Role))
            throw new ActingKeyException("FAH is for admins and owners.");

        var (raw, hash, prefix) = ApiTokenGenerator.Generate();
        var now = DateTime.UtcNow;
        var key = new ApiKey
        {
            OrganizationId = orgId,
            Name = "FAH",
            TokenHash = hash,
            TokenPrefix = prefix,
            Scopes = ApiScopes.Join(ApiKeyAuthenticationHandler.ActingScopes),
            Active = true,
            CreatedAt = now,
            ActingUserId = userId,
            ExpiresAt = now + Lifetime,
        };
        await _keys.AddActingKeyAsync(key);

        return new ActingKey(key.Id, raw, key.ExpiresAt.Value);
    }

    public Task RevokeAsync(string id) => _keys.RevokeActingKeyAsync(id);
}
