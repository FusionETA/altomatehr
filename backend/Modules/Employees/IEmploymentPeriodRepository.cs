using AltomateHR.Api.Modules.Employees.Entities;

namespace AltomateHR.Api.Modules.Employees;

public interface IEmploymentPeriodRepository
{
    // One user's periods in the CURRENT org, newest first — tenant-filtered.
    Task<List<EmploymentPeriod>> GetForUserAsync(string userId);

    // The open period for a user in a NAMED org (ignores the tenant filter —
    // a transfer closes one org's and opens another's).
    Task<EmploymentPeriod?> GetOpenAsync(string organizationId, string userId);

    // Adds WITHOUT saving: the caller's next SaveChanges (on any repository —
    // they share the request's DbContext) writes it together with the change
    // that caused it, so history and profile can't disagree.
    void Stage(EmploymentPeriod period);

    // Whether a tenure ending on `leaveDate` is already recorded for a user in
    // a NAMED org (ignores the tenant filter).
    Task<bool> HasPeriodEndingAsync(string organizationId, string userId, DateTime leaveDate);
}
