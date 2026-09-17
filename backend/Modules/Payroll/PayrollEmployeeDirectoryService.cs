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
        var users = (await _directory.GetUsersAsync())
            .ToDictionary(u => u.Id, u => u, StringComparer.Ordinal);
        var profiles = (await _directory.GetProfilesForCurrentOrgAsync())
            .ToDictionary(p => p.UserId, p => p, StringComparer.Ordinal);

        // Driven by MEMBERSHIPS, not profiles. A member with no EmployeeProfile
        // row simply did not appear here before — which meant the screen whose
        // whole job is "who is not ready to file" silently omitted the people
        // furthest from ready, and an org of twelve showed four.
        var memberships = await _directory.GetMembershipsForCurrentOrgAsync();

        return
        [
            .. memberships
                .Select(m => (Membership: m, Profile: profiles.GetValueOrDefault(m.UserId)))
                // Archived is a property of the profile, so someone without one
                // can never be archived and is always listed.
                .Where(x => includeArchived || x.Profile is null || !x.Profile.IsArchived)
                .Select(x => Map(x.Profile, users.GetValueOrDefault(x.Membership.UserId), x.Membership))
                // By name, because that is what the admin is scanning for.
                .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
        ];
    }

    // `profile` is null for a member who has no EmployeeProfile row yet. Rather
    // than branch through every field, a blank profile stands in: every column
    // then reads as unset, which is exactly what it is, and the readiness
    // checks report all three sections short — also exactly right.
    private static PayrollEmployeeRowDto Map(
        EmployeeProfile? existing,
        User? user,
        OrganizationMembership? membership)
    {
        var profile = existing ?? new EmployeeProfile { UserId = membership?.UserId ?? string.Empty };

        // The same routing PERKESO and LHDN use, and the same one
        // StatutoryEmployeeRow derives: locals and PRs are keyed by IC.
        var isLocalOrPr = profile.HasPr
            || string.Equals(profile.Nationality?.Trim(), "Malaysian", StringComparison.OrdinalIgnoreCase);

        return new PayrollEmployeeRowDto
        {
            EmployeeProfileId = profile.Id,
            HasPayrollProfile = existing is not null,
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
            NotPayableReason = existing is null
                ? "No payroll details yet"
                : profile.IsArchived
                    ? "Archived"
                    : profile.ReportedToLhdn
                        ? "Final payroll already reported to LHDN"
                        : null,

            Missing = PayrollRunReadiness.EmployeeGaps(
                membership?.EmployeeNumber, profile.IdNumber, isLocalOrPr),

            ProfileIncompleteSections = PayrollProfileReadiness.IncompleteSections(profile),
        };
    }
}
