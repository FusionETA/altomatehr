using System.Text.RegularExpressions;
using AltomateHR.Api.Modules.ApiKeys.Dtos;
using AltomateHR.Api.Modules.ApiKeys.Entities;
using AltomateHR.Api.Modules.Audit;

namespace AltomateHR.Api.Modules.ApiKeys;

public class ApiKeyService : IApiKeyService
{
    private readonly IApiKeyRepository _repo;
    // Optional so hand-built instances in tests need not supply it; the app
    // always does.
    private readonly IAuditService? _audit;

    public ApiKeyService(IApiKeyRepository repo, IAuditService? audit = null)
    {
        _repo = repo;
        _audit = audit;
    }

    public async Task<CreatedApiKeyDto> CreateAsync(CreateApiKeyDto dto)
    {
        var scopes = ValidScopes(dto.Scopes);

        var (raw, hash, prefix) = ApiTokenGenerator.Generate();

        var key = new ApiKey
        {
            // OrganizationId left blank → StampTenant sets it to the Owner's active org.
            Name = dto.Name.Trim(),
            TokenHash = hash,
            TokenPrefix = prefix,
            Scopes = ApiScopes.Join(scopes),
            Active = true,
            CreatedAt = DateTime.UtcNow,
        };
        await _repo.AddAsync(key);

        // The ONLY time the raw token leaves the server.
        return new CreatedApiKeyDto
        {
            Id = key.Id,
            Name = key.Name,
            TokenPrefix = key.TokenPrefix,
            Scopes = scopes,
            Active = key.Active,
            CreatedAt = key.CreatedAt,
            LastUsedAt = null,
            Token = raw,
        };
    }

    public async Task<IReadOnlyList<ApiKeyDto>> GetAllAsync()
    {
        var keys = await _repo.GetForCurrentOrgAsync();
        return keys.Select(ToDto).ToList();
    }

    public async Task<ApiKeyDto?> UpdateAsync(string id, UpdateApiKeyDto dto)
    {
        var key = await _repo.GetByIdForCurrentOrgAsync(id);
        if (key is null || key.ActingUserId is not null) return null;
        if (!key.Active)
            throw new InvalidOperationException("This token has been revoked, so its access can't be changed.");

        var scopes = ValidScopes(dto.Scopes);
        var name = dto.Name.Trim();
        if (name.Length == 0) throw new ArgumentException("Give the token a name.");

        var before = ApiScopes.Split(key.Scopes);
        var oldName = key.Name;
        var added = scopes.Except(before).ToList();
        var removed = before.Except(scopes).ToList();
        if (added.Count == 0 && removed.Count == 0 && name == oldName) return ToDto(key);

        key.Name = name;
        key.Scopes = ApiScopes.Join(scopes);
        await _repo.UpdateAsync(key);

        if (_audit is not null)
        {
            var changes = new List<string>();
            if (added.Count > 0) changes.Add($"added {string.Join(", ", added)}");
            if (removed.Count > 0) changes.Add($"removed {string.Join(", ", removed)}");
            if (name != oldName) changes.Add($"renamed from \"{oldName}\"");
            await _audit.WriteAsync(new AuditEvent(
                AuditActions.ApiKeyUpdate,
                $"Changed API token \"{name}\" ({key.TokenPrefix}…): {string.Join("; ", changes)}",
                TargetType: "ApiKey",
                TargetId: key.Id,
                Metadata: new { OldName = oldName, NewName = name, Before = before, After = scopes, Added = added, Removed = removed }));
        }

        return ToDto(key);
    }

    // Validate scopes against the catalog — reject anything unknown up front.
    private static List<string> ValidScopes(IEnumerable<string> requested)
    {
        var scopes = requested.Distinct().ToList();
        var unknown = scopes.Where(s => !ApiScopes.IsKnown(s)).ToList();
        if (unknown.Count > 0)
            throw new ArgumentException($"Unknown scope(s): {string.Join(", ", unknown)}.");
        return scopes;
    }

    public async Task<bool> RevokeAsync(string id)
    {
        var key = await _repo.GetByIdForCurrentOrgAsync(id);
        if (key is null) return false;

        if (key.Active)
        {
            key.Active = false;
            await _repo.UpdateAsync(key);
        }
        return true;
    }

    public async Task<bool> HasAbPayIntegrationAsync()
    {
        var names = await _repo.GetActiveNamesForCurrentOrgAsync();
        return names.Any(IsAbPayKeyName);
    }

    // "ABPay importer", "abpay", "AB Pay sync", "AB-Pay", "ABPayRun" → true. A space,
    // "-" or "_" between AB and Pay is tolerated; nothing else is. "AB" must start a
    // word, so "GrabPay payouts" or "Fab Payments" don't count.
    public static bool IsAbPayKeyName(string? name) =>
        !string.IsNullOrEmpty(name) && AbPayName.IsMatch(name);

    private static readonly Regex AbPayName =
        new(@"(?<![\p{L}\p{N}])ab[\s_-]?pay", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static ApiKeyDto ToDto(ApiKey k) => new()
    {
        Id = k.Id,
        Name = k.Name,
        TokenPrefix = k.TokenPrefix,
        Scopes = ApiScopes.Split(k.Scopes),
        Active = k.Active,
        CreatedAt = k.CreatedAt,
        LastUsedAt = k.LastUsedAt,
    };
}
