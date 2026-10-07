using AltomateHR.Api.Common;
using System.Security.Claims;
using System.Text.Json;
using AltomateHR.Api.Modules.Attendance;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Dtos;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Modules.Policies.Entities;

namespace AltomateHR.Api.Data;

// Demo payslips for the demo company's staff, so the employee portal's
// Payslips page and the admin's payroll screens have real months — with a
// whole team on each run, not one person — to show.
//
// The payslips are produced by the REAL pipeline — create → generate → submit
// → approve on IPayrollRunService — rather than written by hand. That is the
// only way the figures (EPF, SOCSO/EIS, PCB with its year-to-date, the stored
// PCB breakdown, line items) are the ones the engine would actually produce,
// and the only way they stay right when the engine changes.
//
// DEVELOPMENT-ONLY, like DbSeeder, and run by Program.cs inside a scope whose
// HttpContext carries the demo org's Owner — the services read the tenant and
// the actor from ICurrentUser, and at startup there is no request to supply one.
//
// Safe on every boot:
//   - Every staff member's profile (Evan's with his fixed demo values, the
//     rest with made-up ones), their employee IDs, and the company info are
//     created or have BLANK fields filled; nothing typed by hand is
//     overwritten. That makes every demo employee payable, so each run
//     covers the whole team.
//   - January up to last month are seeded, oldest first (runs are submitted
//     in order), and a month that already has a run — whoever made it — is
//     left alone. From JANUARY, not just the last few months: PCB projects
//     the year from its year-to-date, so a year whose first payslip is June
//     is taxed as a mid-year joiner and the demo shows a false RM 0 PCB.
//   - Each run takes everyone payable — the run picker's own rule leaves out
//     a profile that is still incomplete (someone added by hand later), so it
//     can never block the submission.
//   - Any refusal stops the seed quietly: demo data must never stop the API
//     from starting.
public static class DemoPayrollSeeder
{
    private const string DemoOrgId = "org-altomate";
    private const string AdminUserId = "usr-admin";
    private const string EmployeeUserId = "usr-emp";

    public static ClaimsPrincipal DemoOwner() =>
        new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, AdminUserId),
                new Claim("org", DemoOrgId),
                new Claim(ClaimTypes.Role, "Owner"),
                new Claim(ClaimTypes.Role, "Admin"),
            ],
            authenticationType: "DemoSeed"));

    public static async Task SeedAsync(
        IEmployeeProfileRepository profiles,
        IOrganizationMembershipRepository memberships,
        IPayrollCompanyInfoRepository companyInfo,
        IPayrollRunService runs,
        ILogger logger)
    {
        await EnsureEmployeeProfileAsync(profiles);
        await EnsureEmployeeNumberAsync(memberships);
        await EnsureStaffProfilesAsync(profiles, memberships);
        await EnsureCompanyInfoAsync(companyInfo);

        // January to last month of last month's year, in Malaysian time — in
        // January itself, that is the whole of the previous year.
        var today = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(
            DateTime.UtcNow, AttendanceTime.DefaultTimeZone);
        var lastMonth = new DateTime(today.Year, today.Month, 1).AddMonths(-1);

        for (var month = 1; month <= lastMonth.Month; month++)
        {
            var period = new DateTime(lastMonth.Year, month, 1);
            var label = $"{period:MMMM yyyy}";

            var created = await runs.CreateAsync(new CreatePayrollRunDto
            {
                PeriodYear = period.Year,
                PeriodMonth = period.Month,
            });

            // Already a run for this month — hand-made or seeded earlier. Leave it.
            if (!created.Ok || created.Run is null) continue;

            var generated = await runs.GenerateAsync(created.Run.Id);
            if (!generated.Ok)
            {
                logger.LogWarning("Demo payroll: could not run {Period}: {Error}", label, generated.Error);
                return;
            }

            var submitted = await runs.SubmitForApprovalAsync(created.Run.Id);
            if (!submitted.Ok)
            {
                logger.LogWarning("Demo payroll: could not submit {Period}: {Error}", label, submitted.Error);
                return;
            }

            var approved = await runs.ApproveAsync(created.Run.Id);
            if (!approved.Ok)
            {
                logger.LogWarning("Demo payroll: could not approve {Period}: {Error}", label, approved.Error);
                return;
            }

            logger.LogInformation("Demo payroll: seeded and approved {Period}", label);
        }
    }

    // A complete payroll profile — every field PayrollProfileReadiness gates,
    // so a run includes him. Identifiers are obviously fake.
    private static async Task<EmployeeProfile> EnsureEmployeeProfileAsync(IEmployeeProfileRepository profiles)
    {
        var existing = await profiles.GetByUserAsync(EmployeeUserId);
        var p = existing ?? new EmployeeProfile
        {
            OrganizationId = DemoOrgId,
            UserId = EmployeeUserId,
            CreatedAt = DateTime.UtcNow,
        };

        p.Gender ??= Gender.MALE;
        p.DateOfBirth ??= new DateTime(1990, 1, 1);
        p.Nationality ??= "Malaysian";
        p.IdType ??= IdType.NRIC;
        p.IdNumber ??= "900101-01-0001";
        p.MaritalStatus ??= MaritalStatus.SINGLE;
        p.JoinDate ??= new DateTime(2025, 1, 6);
        p.Department ??= "Operations";

        if (p.SalaryType == SalaryType.MONTHLY) p.MonthlySalary ??= 5500m;
        if (p.EpfEmployeeRate == 0m) p.EpfEmployeeRate = 11m;
        p.EpfNumber ??= "10000001";
        p.SocsoNumber ??= "900101010001";
        p.SocsoScheme ??= SocsoScheme.EMPLOYMENT_INJURY_INVALIDITY;
        p.IncomeTaxNumber ??= "IG00000000010";

        p.BankName ??= "Maybank";
        p.BankAccountHolderName ??= "Evan Employee";
        p.BankAccountNumber ??= "514000000001";

        // One recurring allowance, so the payslip shows more than basic pay.
        p.FixedAllowancesJson ??= JsonSerializer.Serialize(
            new[] { new { category = "allowance_standard", name = "Phone allowance", amount = 150m } });

        p.UpdatedAt = DateTime.UtcNow;

        if (existing is null) return await profiles.AddAsync(p);
        await profiles.UpdateAsync(p);
        return p;
    }

    // The rest of the demo staff — Employees and Supervisors other than Evan —
    // made payable with obviously fake values, varied per person so the run
    // reads like a team: salary, birthday, bank. Blanks only, like Evan's.
    private static async Task EnsureStaffProfilesAsync(
        IEmployeeProfileRepository profiles, IOrganizationMembershipRepository memberships)
    {
        string[] banks = ["Maybank", "CIMB Bank", "Public Bank", "RHB Bank", "Hong Leong Bank"];
        var staff = (await memberships.GetForCurrentOrgAsync())
            .Where(m => m.UserId != EmployeeUserId && OrgRoles.IsOnPayroll(m.Role))
            .OrderBy(m => m.UserId, StringComparer.Ordinal)
            .ToList();

        var i = 0;
        foreach (var m in staff)
        {
            i++;
            if (string.IsNullOrWhiteSpace(m.EmployeeNumber))
            {
                m.EmployeeNumber = EmployeeNumbers.Next((await memberships.GetForCurrentOrgAsync()).Select(x => x.EmployeeNumber));
                m.UpdatedAt = DateTime.UtcNow;
                await memberships.UpdateAsync(m);
            }

            var existing = await profiles.GetByUserAsync(m.UserId);
            var p = existing ?? new EmployeeProfile
            {
                OrganizationId = DemoOrgId,
                UserId = m.UserId,
                CreatedAt = DateTime.UtcNow,
            };

            var dob = new DateTime(1985 + (i * 3) % 15, 1 + i % 12, 1 + (i * 7) % 27);
            var ic = $"{dob:yyMMdd}-14-{5000 + i:0000}";

            p.Gender ??= i % 2 == 1 ? Gender.MALE : Gender.FEMALE;
            p.DateOfBirth ??= dob;
            p.Nationality ??= "Malaysian";
            p.IdType ??= IdType.NRIC;
            p.IdNumber ??= ic;
            p.MaritalStatus ??= MaritalStatus.SINGLE;
            p.JoinDate ??= new DateTime(2025, 1, 6);
            p.Phone ??= $"+6012-{3000000 + i * 1111:0000000}";
            p.AddressLine1 ??= $"{10 + i} Jalan Demo {i}";
            p.City ??= "Kuala Lumpur";
            p.Postcode ??= "50450";
            p.State ??= "Kuala Lumpur";

            if (p.SalaryType == SalaryType.MONTHLY) p.MonthlySalary ??= 3000m + i * 250m;
            if (p.EpfEmployeeRate == 0m) p.EpfEmployeeRate = 11m;
            p.EpfNumber ??= $"{20000000 + i}";
            p.SocsoNumber ??= ic.Replace("-", "");
            p.SocsoScheme ??= SocsoScheme.EMPLOYMENT_INJURY_INVALIDITY;
            p.IncomeTaxNumber ??= $"IG{30000000000L + i}";

            p.BankName ??= banks[i % banks.Length];
            p.BankAccountHolderName ??= $"DEMO EMPLOYEE {i}";
            p.BankAccountNumber ??= $"{51400000000L + i * 7}";

            p.UpdatedAt = DateTime.UtcNow;
            if (existing is null) await profiles.AddAsync(p);
            else await profiles.UpdateAsync(p);
        }
    }

    // CP39's mandatory employee-number column — submission refuses without it.
    private static async Task EnsureEmployeeNumberAsync(IOrganizationMembershipRepository memberships)
    {
        var membership = await memberships.GetAsync(DemoOrgId, EmployeeUserId);
        if (membership is null || !string.IsNullOrWhiteSpace(membership.EmployeeNumber)) return;

        membership.EmployeeNumber = "DEMO-001";
        membership.UpdatedAt = DateTime.UtcNow;
        await memberships.UpdateAsync(membership);
    }

    // The four Company Info fields submission requires. Blanks only.
    private static async Task EnsureCompanyInfoAsync(IPayrollCompanyInfoRepository companyInfo)
    {
        var existing = await companyInfo.GetAsync();
        var info = existing ?? new PayrollCompanyInfo { OrganizationId = DemoOrgId };

        if (string.IsNullOrWhiteSpace(info.EmployerName)) info.EmployerName = "AltomateHR Demo Co Sdn Bhd";
        if (string.IsNullOrWhiteSpace(info.EmployerTin)) info.EmployerTin = "E9100000017";
        if (string.IsNullOrWhiteSpace(info.RegistrationNo)) info.RegistrationNo = "202001000001";
        if (string.IsNullOrWhiteSpace(info.PerkesoEmployerCode)) info.PerkesoEmployerCode = "D9100000017";

        if (existing is null) await companyInfo.AddAsync(info);
        else await companyInfo.UpdateAsync(info);
    }
}
