using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

// The chosen roster for a run — see PayrollRunMember.
public interface IPayrollRunMemberRepository
{
    Task<List<PayrollRunMember>> GetForRunAsync(string payrollRunId);

    // Replace the whole selection in one shot — used when a draft is created.
    Task ReplaceForRunAsync(string payrollRunId, IEnumerable<string> employeeProfileIds);
}
