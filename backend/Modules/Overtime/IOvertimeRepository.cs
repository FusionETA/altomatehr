using AltomateHR.Api.Modules.Overtime.Entities;

namespace AltomateHR.Api.Modules.Overtime;

public interface IOvertimeRepository
{
    Task<List<OvertimeRequest>> GetAllAsync();
    Task<OvertimeRequest?> GetByIdAsync(string id);
    Task<List<OvertimeRequest>> GetByEmployeeAsync(string employeeId);
    Task<OvertimeRequest?> GetByPhotoUrlAsync(string photoUrl);
    Task<OvertimeRequest> AddAsync(OvertimeRequest request);
    Task UpdateAsync(OvertimeRequest request);
}
