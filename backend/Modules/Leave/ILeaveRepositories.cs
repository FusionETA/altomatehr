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

public interface ILeaveApplicationRepository
{
    Task<List<LeaveApplication>> GetAllAsync();
    Task<LeaveApplication?> GetByIdAsync(string id);
    Task<List<LeaveApplication>> GetByEmployeeAsync(string employeeId);
    Task<LeaveApplication> AddAsync(LeaveApplication application);
    Task UpdateAsync(LeaveApplication application);
}
