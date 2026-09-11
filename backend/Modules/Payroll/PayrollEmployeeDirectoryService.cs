using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Payroll.Dtos;

namespace AltomateHR.Api.Modules.Payroll;

// Reads the roster through IDirectoryService, like the rest of this module —
// the profile is Employees' data and its repository stays private.
//
// Read-only by design. Every field here is edited on the employee's own
// profile or through the bulk import next door; offering a second place to
// change a salary is how two screens start disagreeing about one.
public class PayrollEmployeeDirectoryService : IPayrollEmployeeDirectoryService
{
    private readonly IDirectoryService _directory;

    public PayrollEmployeeDirectoryService(IDirectoryService directory) => _directory = directory;

    public async Task<IReadOnlyList<PayrollEmployeeRowDto>> GetAllAsync(bool includeArchived = false)
    {
        var profiles = await _directory.GetProfilesForCurrentOrgAsync();
        var users = (await _directory.GetUsersAsync())
            .ToDictionary(u => u.Id, u => u, StringComparer.Ordinal);
        var memberships = (await _directory.GetMembershipsForCurrentOrgAsync())
            .ToDictionary(m => m.UserId, m => m, StringComparer.Ordinal);

        return
        [
            .. profiles
                .Where(p => includeArchived || !p.IsArchived)
                .Select(p => Map(p, users.GetValueOrDefault(p.UserId), memberships.GetValueOrDefault(p.UserId)))
                // By name, because that is what the admin is scanning for.
                .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
        ];
    }

    private static PayrollEmployeeRowDto Map(
        EmployeeProfile profile,
        User? user,
        OrganizationMembership? membership)
    {
        // The same routing PERKESO and LHDN use, and the same one
        // StatutoryEmployeeRow derives: locals and PRs are keyed by IC.
        var isLocalOrPr = profile.HasPr
            || string.Equals(profile.Nationality?.Trim(), "Malaysian", StringComparison.OrdinalIgnoreCase);

        return new PayrollEmployeeRowDto
        {
            EmployeeProfileId = profile.Id,
            UserId = profile.UserId,
            Name = user?.Name ?? string.Empty,
            Email = user?.Email ?? string.Empty,
            EmployeeNumber = membership?.EmployeeNumber,
            JobTitle = membership?.JobTitle,
            Department = profile.Department,

            SalaryType = profile.SalaryType,
            MonthlySalary = profile.MonthlySalary,
            HourlyRate = profile.HourlyRate,

            ContributeToEpf = profile.ContributeToEpf,
            EpfNumber = profile.EpfNumber,
            EpfEmployeeRate = profile.EpfEmployeeRate,
            SocsoNumber = profile.SocsoNumber,
            ContributeToEis = profile.ContributeToEis,
            IncomeTaxNumber = profile.IncomeTaxNumber,

            PaymentMethod = profile.PaymentMethod,
            BankName = profile.BankName,
            BankAccountNumber = profile.BankAccountNumber,

            IdNumber = profile.IdNumber,
            IdType = profile.IdType,
            Nationality = profile.Nationality,
            HasPr = profile.HasPr,
            IsResident = profile.IsResident,

            JoinDate = profile.JoinDate,
            LeaveDate = profile.LeaveDate,
            IsArchived = profile.IsArchived,

            // The period-dependent reasons (joined after, left before) are
            // deliberately not here — this list belongs to no month. Only the
            // two that hold whatever period is being run.
            NotPayableReason = profile.IsArchived
                ? "Archived"
                : profile.ReportedToLhdn
                    ? "Final payroll already reported to LHDN"
                    : null,

            Missing = PayrollRunReadiness.EmployeeGaps(
                membership?.EmployeeNumber, profile.IdNumber, isLocalOrPr),
        };
    }
}
