using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Partners.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Partners;

public class ApiClientRepository : IApiClientRepository
{
    private readonly AppDbContext _db;

    public ApiClientRepository(AppDbContext db) => _db = db;

    public Task<ApiClient?> GetByIdAsync(string id) =>
        _db.ApiClients.FirstOrDefaultAsync(c => c.Id == id);

    public Task<ApiClient?> GetByNameAsync(string name) =>
        _db.ApiClients.FirstOrDefaultAsync(c => c.Name == name);

    public Task<ApiClient?> GetBySecretHashAsync(string secretHash) =>
        _db.ApiClients.FirstOrDefaultAsync(c => c.SecretHash == secretHash);

    public async Task<ApiClient> AddAsync(ApiClient client)
    {
        var now = DateTime.UtcNow;
        client.CreatedAt = now;
        client.UpdatedAt = now;
        _db.ApiClients.Add(client);
        await _db.SaveChangesAsync();
        return client;
    }
}
