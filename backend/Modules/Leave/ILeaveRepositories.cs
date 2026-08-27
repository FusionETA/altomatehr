using AltomateHR.Api.Modules.Leave.Entities;

namespace AltomateHR.Api.Modules.Leave;

public interface ILeaveTypeRepository
{
    Task<List<LeaveType>> GetAllAsync();
    Task<LeaveType?> GetByIdAsync(string id);
    Task<LeaveType?> GetByCodeAsync(string code);
    Task<LeaveType> AddAsync(LeaveType type);
    Task UpdateAsync(LeaveType type);
}

// Entitlement rows are read by the CRONS, which run unauthenticated — so the
// tenant filter is a no-op and these see EVERY org. That's intended: the crons
// advance accrual for the whole system. Writes must therefore set
// OrganizationId explicitly (auto-stamping is also a no-op without a caller).
public interface ILeaveEntitlementRepository
{
    Task<List<LeaveEntitlement>> GetByYearAsync(int year);
    Task<List<LeaveEntitlement>> GetForEmployeeYearAsync(string employeeId, int year);
    Task<List<LeaveEntitlement>> GetCarryExpiringAsync(DateTime asOf);
    Task AddAsync(LeaveEntitlement entitlement);
    Task SaveAsync();
}

public interface ILeaveApplicationRepository
{
    Task<List<LeaveApplication>> GetAllAsync();
    Task<LeaveApplication?> GetByIdAsync(string id);
    Task<List<LeaveApplication>> GetByEmployeeAsync(string employeeId);
    Task<LeaveApplication?> GetByXeroFileIdAsync(string xeroFileId);
    Task<LeaveApplication> AddAsync(LeaveApplication application);
    Task UpdateAsync(LeaveApplication application);
}
