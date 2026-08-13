using AltomateHR.Api.Modules.Claims.Entities;

namespace AltomateHR.Api.Modules.Claims;

public interface IClaimsRepository
{
    Task<List<Claim>> GetAllAsync();
    Task<List<Claim>> GetByEmployeeIdAsync(string employeeId);
    Task<Claim?> GetByIdAsync(string id);
    Task<Claim?> GetByReceiptUrlAsync(string receiptUrl);
    Task<Claim> AddAsync(Claim claim);
    Task UpdateAsync(Claim claim);
    Task<bool> DeleteAsync(string id);
}
