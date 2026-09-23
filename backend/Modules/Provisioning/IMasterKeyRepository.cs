namespace AltomateHR.Api.Modules.Provisioning;

public interface IMasterKeyRepository
{
    // By hash, never by raw token — the raw value is not stored.
    Task<MasterKey?> GetByHashAsync(string tokenHash);

    Task<List<MasterKey>> GetAllAsync();
    Task<MasterKey> AddAsync(MasterKey key);
    Task UpdateAsync(MasterKey key);
}
