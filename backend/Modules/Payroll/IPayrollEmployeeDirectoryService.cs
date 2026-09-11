using AltomateHR.Api.Modules.Payroll.Dtos;

namespace AltomateHR.Api.Modules.Payroll;

// The payroll roster: everyone in the org, as payroll needs to see them.
public interface IPayrollEmployeeDirectoryService
{
    // `includeArchived` is off by default — an archived profile is not paid,
    // so it would only pad the list an admin is checking before a run. It is
    // still offered because a leaver's details are read at year end.
    Task<IReadOnlyList<PayrollEmployeeRowDto>> GetAllAsync(bool includeArchived = false);
}
