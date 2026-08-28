using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace AltomateHR.Api.Modules.Partners;

// Redis-backed (via IDistributedCache) ticket + token store. In dev without a
// Redis connection string, Program.cs wires an in-memory IDistributedCache instead,
// so this code is identical either way.
//
// Access/refresh tokens are keyed by their HASH, never their raw value — a Redis
// key dump doesn't hand out usable tokens. TTL is Redis's job (EX on write).
public class PartnerAuthStore : IPartnerAuthStore
{
    private const string TicketKey  = "partner:ticket:";
    private const string AccessKey  = "partner:access:";
    private const string RefreshKey = "partner:refresh:";

    private readonly IDistributedCache _cache;

    public PartnerAuthStore(IDistributedCache cache) => _cache = cache;

    public async Task<string> MintTicketAsync(PartnerTicketData data, TimeSpan ttl)
    {
        var ticket = PartnerTokenGenerator.NewTicket();
        await SetAsync(TicketKey + ticket, data, ttl);   // ticket id is random + short-lived → safe as the key
        return ticket;
    }

    public async Task<PartnerTicketData?> RedeemTicketAsync(string ticket)
    {
        if (string.IsNullOrWhiteSpace(ticket)) return null;
        var key = TicketKey + ticket;
        var data = await GetAsync<PartnerTicketData>(key);
        if (data is not null) await _cache.RemoveAsync(key);   // single-use
        return data;
    }

    public Task StoreAccessTokenAsync(string token, PartnerTokenData data, TimeSpan ttl) =>
        SetAsync(AccessKey + PartnerTokenGenerator.Hash(token), data, ttl);

    public Task<PartnerTokenData?> GetAccessTokenAsync(string token) =>
        string.IsNullOrWhiteSpace(token)
            ? Task.FromResult<PartnerTokenData?>(null)
            : GetAsync<PartnerTokenData>(AccessKey + PartnerTokenGenerator.Hash(token));

    public Task StoreRefreshTokenAsync(string token, PartnerTokenData data, TimeSpan ttl) =>
        SetAsync(RefreshKey + PartnerTokenGenerator.Hash(token), data, ttl);

    public async Task<PartnerTokenData?> RedeemRefreshTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var key = RefreshKey + PartnerTokenGenerator.Hash(token);
        var data = await GetAsync<PartnerTokenData>(key);
        if (data is not null) await _cache.RemoveAsync(key);   // rotation: the old refresh token dies
        return data;
    }

    private Task SetAsync<T>(string key, T value, TimeSpan ttl) =>
        _cache.SetStringAsync(key, JsonSerializer.Serialize(value),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl });

    private async Task<T?> GetAsync<T>(string key)
    {
        var json = await _cache.GetStringAsync(key);
        return json is null ? default : JsonSerializer.Deserialize<T>(json);
    }
}
