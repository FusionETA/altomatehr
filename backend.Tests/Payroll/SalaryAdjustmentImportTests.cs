using System.Text;
using AltomateHR.Api.Common.Tabular;
using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Modules.Policies.Entities;
using AltomateHR.Api.Tests.Audit;
using AltomateHR.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Tests.Payroll;

// New salaries for many people at once. What matters, because getting it
// wrong is silent: each change lands in the salary history with ITS date and
// reason; a future date is refused (the salary applies straight away); a
// first salary is not a "raise"; and one bad row saves nothing.
public class SalaryAdjustmentImportTests : IDisposable
{
    private static readonly DateTime Today = new(2026, 6, 15);

    private readonly AppDbContext _db;
    private readonly StubCurrentUser _currentUser = new() { UserId = "usr-admin" };
    private readonly SalaryChangeService _history;
    private readonly SalaryAdjustmentImportService _import;

    public SalaryAdjustmentImportTests()
    {
        _db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"salary-import-{Guid.NewGuid()}").Options,
            _currentUser);

        var directory = TestDirectory.Over(
            new OrganizationMembershipRepository(_db),
            new UserRepository(_db),
            new EmployeeProfileRepository(_db));
        var audit = new FakeAuditService();

        _history = new SalaryChangeService(
            new SalaryChangeRepository(_db),
            new PayrollRunRepository(_db),
            new PayslipRepository(_db),
            new PayrollRunAdjustmentRepository(_db),
            new PayrollSettingsService(new PayrollSettingsRepository(_db), audit),
            directory,
            _currentUser,
            audit);

        _import = new SalaryAdjustmentImportService(
            directory, new EmployeeProfileRepository(_db), _history, today: () => Today);

        Seed();
    }

    public void Dispose() => _db.Dispose();

    private void Seed()
    {
        void Person(string id, string name, string number, string role, SalaryType type,
            decimal? monthly = null, decimal? hourly = null, bool archived = false)
        {
            _db.Users.Add(new User { Id = id, Email = $"{id}@x.com", Name = name });
            _db.OrganizationMemberships.Add(new OrganizationMembership
            {
                OrganizationId = "org-1", UserId = id, Role = role, EmployeeNumber = number,
            });
            _db.EmployeeProfiles.Add(new EmployeeProfile
            {
                Id = $"emp-{id}", OrganizationId = "org-1", UserId = id, SalaryType = type,
                MonthlySalary = monthly, HourlyRate = hourly, IsArchived = archived,
            });
        }

        Person("usr-1", "Aisyah", "E-001", "Employee", SalaryType.MONTHLY, monthly: 5000m);
        Person("usr-2", "Bala", "E-002", "Supervisor", SalaryType.HOURLY, hourly: 20m);
        Person("usr-3", "Chan", "E-003", "Employee", SalaryType.MONTHLY);                  // no salary yet
        Person("usr-4", "Dewi", "E-004", "Employee", SalaryType.MONTHLY, monthly: 4000m, archived: true);
        Person("usr-admin", "The Admin", "A-001", "Admin", SalaryType.MONTHLY, monthly: 9000m);
        _db.SaveChanges();
    }

    private static byte[] Csv(params string[] rows) => Encoding.UTF8.GetBytes(
        "Employee No,Email,New Salary,Effective Date,Reason,Notes\n" + string.Join("\n", rows) + "\n");

    private Task<SalaryAdjustmentImportResult> Import(params string[] rows) =>
        _import.ImportAsync(Csv(rows), TabularFormat.Csv);

    private decimal? Monthly(string userId) => _db.EmployeeProfiles.Single(p => p.UserId == userId).MonthlySalary;

    // ─── Applying ───────────────────────────────────────────────────────

    [Fact]
    public async Task ARaise_ChangesTheSalary_AndIsRecordedWithItsDateAndReason()
    {
        var result = await Import("E-001,,5500,2026-06-01,PROMOTION,Promoted to senior");

        Assert.True(result.Ok, string.Join(" | ", result.Errors.Select(e => e.Message)));
        Assert.Equal(1, result.Changed);
        Assert.Equal(5500m, Monthly("usr-1"));
        var change = Assert.Single(await _history.GetForEmployeeAsync("emp-usr-1"));
        Assert.Equal(new DateTime(2026, 6, 1), change.EffectiveDate);
        Assert.Equal(SalaryChangeReason.PROMOTION, change.Reason);
        Assert.Equal("Promoted to senior", change.Notes);
        Assert.Equal(5000m, change.PreviousMonthlySalary);
    }

    [Fact]
    public async Task BlankDateAndReason_MeanTodayAndRaise()
    {
        await Import("E-001,,5200,,,");

        var change = Assert.Single(await _history.GetForEmployeeAsync("emp-usr-1"));
        Assert.Equal(Today, change.EffectiveDate);
        Assert.Equal(SalaryChangeReason.RAISE, change.Reason);
    }

    // The whole roster rides along in the template; only filled rows count.
    [Fact]
    public async Task ABlankNewSalary_LeavesThatPersonAlone_AndTheSameSalaryIsUnchanged()
    {
        var result = await Import("E-001,,5000,,,", "E-002,,,,,");

        Assert.True(result.Ok);
        Assert.Equal(0, result.Changed);
        Assert.Equal(1, result.Unchanged);
        Assert.Empty(_db.SalaryChanges);
    }

    // The amount is the person's own basis — never a monthly figure written
    // onto someone paid by the hour.
    [Fact]
    public async Task ForHourlyStaff_TheNewSalaryIsTheHourlyRate()
    {
        await Import("E-002,,22.50,2026-06-01,RAISE,");

        var profile = _db.EmployeeProfiles.Single(p => p.UserId == "usr-2");
        Assert.Equal(22.50m, profile.HourlyRate);
        Assert.Null(profile.MonthlySalary);
        Assert.Equal(22.50m, Assert.Single(await _history.GetForEmployeeAsync("emp-usr-2")).NewHourlyRate);
    }

    // As on the employee screen: filling in an empty salary is not a raise.
    [Fact]
    public async Task AFirstSalary_IsSet_ButNotRecordedAsAChange()
    {
        var result = await Import("E-003,,3000,,,");

        Assert.Equal(1, result.FirstSalaries);
        Assert.Equal(3000m, Monthly("usr-3"));
        Assert.Empty(_db.SalaryChanges);
    }

    [Fact]
    public async Task AnEmailIdentifiesSomeoneWithoutAnEmployeeNumber()
    {
        var result = await Import(",usr-1@x.com,5100,,,");

        Assert.True(result.Ok);
        Assert.Equal(5100m, Monthly("usr-1"));
    }

    // ─── Refusing ───────────────────────────────────────────────────────

    // v2 has one current salary, applied straight away: a raise dated next
    // month would already be paid in this month's run.
    [Fact]
    public async Task AFutureEffectiveDate_IsRefused()
    {
        var result = await Import("E-001,,6000,2026-07-01,RAISE,");

        Assert.False(result.Ok);
        Assert.Contains("in the future", Assert.Single(result.Errors).Message);
        Assert.Equal(5000m, Monthly("usr-1"));
    }

    // One bad row saves nothing — including the good rows — and every
    // problem is reported at once.
    [Fact]
    public async Task AnyBadRow_SavesNothing_AndEveryProblemIsListed()
    {
        var result = await Import(
            "E-001,,5500,,,",                // fine on its own
            "E-999,,5000,,,",                // nobody
            "E-002,,-5,someday,BONUS,",      // three problems on one row
            "E-004,,4500,,,",                // archived
            "A-001,,9999,,,");               // an admin is not on payroll

        Assert.False(result.Ok);
        Assert.Equal(4, result.Errors.Count);
        Assert.Contains(result.Errors, e => e.Message.Contains("\"E-999\""));
        var bala = Assert.Single(result.Errors, e => e.Message.StartsWith("Bala")).Message;
        Assert.Contains("above 0", bala);
        Assert.Contains("isn't a date", bala);
        Assert.Contains("\"BONUS\"", bala);
        Assert.Contains(result.Errors, e => e.Message.Contains("archived"));
        Assert.Equal(5000m, Monthly("usr-1"));
        Assert.Empty(_db.SalaryChanges);
    }

    [Fact]
    public async Task TheSamePersonTwice_IsRefused()
    {
        var result = await Import("E-001,,5500,,,", ",usr-1@x.com,5600,,,");

        Assert.False(result.Ok);
        Assert.Contains("more than once", Assert.Single(result.Errors).Message);
    }

    [Fact]
    public async Task ANumberAndAnEmailForDifferentPeople_IsRefused()
    {
        var result = await Import("E-001,usr-2@x.com,5500,,,");

        Assert.Contains("but the email", Assert.Single(result.Errors).Message);
    }

    // ─── Template ───────────────────────────────────────────────────────

    // Pre-filled with the people payroll pays (not archived, not admins),
    // their current salary shown, and it imports back as "nothing changed".
    [Fact]
    public async Task TheTemplate_ListsPayrollStaff_AndImportsBackAsNoChange()
    {
        var template = await _import.BuildTemplateAsync();
        var sheets = TabularReader.ReadAllSheets(template.Content, TabularFormat.Xlsx);
        var data = sheets.Single(s => s.Name == SalaryAdjustmentImportSheet.SheetName).Rows;

        var names = data.Skip(1).Select(r => r[2]).ToList();
        Assert.Equal(["Aisyah", "Bala", "Chan"], names);
        Assert.Equal("5000.00", data[1][4]);

        var result = await _import.ImportAsync(template.Content, TabularFormat.Xlsx);
        Assert.True(result.Ok);
        Assert.Equal(0, result.Changed + result.FirstSalaries + result.Unchanged);
    }
}
