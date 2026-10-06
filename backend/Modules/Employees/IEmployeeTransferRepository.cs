using AltomateHR.Api.Modules.Employees.Entities;

namespace AltomateHR.Api.Modules.Employees;

public interface IEmployeeTransferRepository
{
    // The one queued transfer (PENDING, or FAILED and awaiting retry) for a
    // user in the CURRENT org — tenant-filtered.
    Task<EmployeeTransfer?> GetOpenForUserAsync(string userId);

    // Every queued transfer in the CURRENT org, for the employee list's chip.
    Task<List<EmployeeTransfer>> GetOpenForCurrentOrgAsync();

    // By id within the CURRENT org — tenant-filtered.
    Task<EmployeeTransfer?> GetByIdAsync(string id);

    // By id in ANY org. The daily job runs with no org; nothing else may use this.
    Task<EmployeeTransfer?> GetByIdAnyOrgAsync(string id);

    // Ids of queued transfers whose effective date is on or before `today`,
    // across every org (the daily job), oldest first.
    Task<List<string>> GetDueIdsAsync(DateTime today, int max);

    Task AddAsync(EmployeeTransfer transfer);
    Task UpdateAsync(EmployeeTransfer transfer);

    // Writes an executed transfer in ONE SaveChanges, so the move lands whole
    // or not at all: the new rows passed here are added, and every change the
    // service made to rows it loaded (source profile archive, reused target
    // membership/profile, the transfer itself) rides along — they share this
    // request's DbContext. `isNewTransfer` = executed inline at creation.
    Task CommitExecutionAsync(
        EmployeeTransfer transfer,
        bool isNewTransfer,
        IEnumerable<OrganizationMembership> newMemberships,
        IEnumerable<EmployeeProfile> newProfiles);
}
