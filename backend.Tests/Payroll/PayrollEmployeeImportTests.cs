using System.Text;
using AltomateHR.Api.Common.Tabular;
using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Policies.Entities;
using AltomateHR.Api.Tests.Audit;
using AltomateHR.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Tests.Payroll;

// Bulk-filling the statutory and salary details that make someone payable.
//
// Built on the same shared import machinery Attendance, Leave and Claims
// use, so what is pinned here is the payroll-specific behaviour: a blank
// cell leaves a field alone, a bad amount is reported rather than ignored,
// and the export round-trips back through the import.
public class PayrollEmployeeImportTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly StubCurrentUser _currentUser = new();
    private readonly FakeAuditService _audit = new();
    private readonly PayrollEmployeeImportService _service;

    public PayrollEmployeeImportTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"payroll-emp-import-{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options, _currentUser);

        var profiles = new EmployeeProfileRepository(_db);
        var directory = TestDirectory.Over(
            new OrganizationMembershipRepository(_db),
            new UserRepository(_db),
            profiles);

        _service = new PayrollEmployeeImportService(
            profiles, new EmployeeRowResolver(directory), directory, _audit);

        Seed();
    }

    public void Dispose() => _db.Dispose();

    private void Seed()
    {
        _db.Users.Add(new User { Id = "usr-1", Email = "aisyah@x.com", Name = "Aisyah Binti Rahman" });
        _db.Users.Add(new User { Id = "usr-2", Email = "bala@x.com", Name = "Bala" });
        _db.OrganizationMemberships.Add(new OrganizationMembership
        {
            Id = "mem-1", OrganizationId = "org-1", UserId = "usr-1", Role = "Employee",
        });
        _db.OrganizationMemberships.Add(new OrganizationMembership
        {
            Id = "mem-2", OrganizationId = "org-1", UserId = "usr-2", Role = "Employee",
        });
        _db.EmployeeProfiles.Add(new EmployeeProfile
        {
            Id = "emp-1", OrganizationId = "org-1", UserId = "usr-1",
            MonthlySalary = 4000m, EpfNumber = "1111111", Nationality = "Malaysian",
        });
        _db.SaveChanges();
    }

    // Only the columns a given test cares about — the importer matches by
    // header text, so a partial sheet is a first-class case.
    private static byte[] Csv(params string[] lines) =>
        Encoding.UTF8.GetBytes(string.Join("\n", lines));

    private Task<TabularImportResult> Import(params string[] lines) =>
        _service.ImportAsync(Csv(lines), TabularFormat.Csv);

    private EmployeeProfile Profile(string id = "emp-1") =>
        _db.EmployeeProfiles.AsNoTracking().Single(p => p.Id == id);

    // ─── Importing ──────────────────────────────────────────────────────

    [Fact]
    public async Task AMatchedRowUpdatesTheProfile()
    {
        var result = await Import(
            "Employee Email,Monthly Salary,EPF No,Income Tax No",
            "aisyah@x.com,5500.00,7654321,SG12345678901");

        Assert.Equal(1, result.Imported);
        Assert.Empty(result.Errors);

        var profile = Profile();
        Assert.Equal(5500m, profile.MonthlySalary);
        Assert.Equal("7654321", profile.EpfNumber);
        Assert.Equal("SG12345678901", profile.IncomeTaxNumber);
    }

    // "Malaysia" is what a spreadsheet says; the CP39 file keys locals on an
    // exact "Malaysian", so the import maps it onto the dropdown's spelling.
    [Theory]
    [InlineData("Malaysia", "Malaysian")]
    [InlineData("CHINA", "Chinese")]
    [InlineData("warganegara malaysia", "Malaysian")]
    public async Task ANationalityIsMappedOntoTheList(string cell, string saved)
    {
        var result = await Import("Employee Email,Nationality", $"aisyah@x.com,{cell}");

        Assert.Equal(1, result.Imported);
        Assert.Empty(result.Warnings);
        Assert.Equal(saved, Profile().Nationality);
    }

    // Never guessed at: kept as typed, and the row still lands, with a note
    // naming it. (This exact text is the previous system's template hint,
    // which that system imported as three people's nationality.)
    [Fact]
    public async Task AnUnrecognisedNationalityIsKeptAndReported()
    {
        var result = await Import(
            "Employee Email,Nationality", "aisyah@x.com,\"Malaysian / Indonesian / etc.\"");

        Assert.Equal(1, result.Imported);
        Assert.Equal(0, result.Failed);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal(2, warning.Row);
        Assert.Contains("Malaysian / Indonesian / etc.", warning.Message);
        Assert.Equal("Malaysian / Indonesian / etc.", Profile().Nationality);
    }

    // The reason a partial sheet is safe: "here are everyone's bank details"
    // must not wipe the statutory numbers already on file.
    [Fact]
    public async Task ABlankCellLeavesTheFieldUnchanged()
    {
        await Import(
            "Employee Email,Monthly Salary,EPF No",
            "aisyah@x.com,,");

        var profile = Profile();
        Assert.Equal(4000m, profile.MonthlySalary);
        Assert.Equal("1111111", profile.EpfNumber);
        Assert.Equal("Malaysian", profile.Nationality);
    }

    [Fact]
    public async Task EnumsAndDatesAreRead()
    {
        await Import(
            "Employee Email,Salary Type,ID Type,Marital Status,Join Date",
            "aisyah@x.com,HOURLY,NRIC,MARRIED,2024-03-01");

        var profile = Profile();
        Assert.Equal(SalaryType.HOURLY, profile.SalaryType);
        Assert.Equal(IdType.NRIC, profile.IdType);
        Assert.Equal(MaritalStatus.MARRIED, profile.MaritalStatus);
        Assert.Equal(new DateTime(2024, 3, 1), profile.JoinDate);
    }

    [Theory]
    [InlineData("Yes", true)]
    [InlineData("no", false)]
    [InlineData("TRUE", true)]
    [InlineData("0", false)]
    public async Task FlagsAreReadGenerously(string raw, bool expected)
    {
        await Import(
            "Employee Email,Contribute to EPF",
            $"aisyah@x.com,{raw}");

        Assert.Equal(expected, Profile().ContributeToEpf);
    }

    // A column header from another system still lands, via its alias.
    [Fact]
    public async Task AColumnAliasIsAccepted()
    {
        await Import(
            "Employee Email,Basic Salary,KWSP No",
            "aisyah@x.com,6000,9999999");

        var profile = Profile();
        Assert.Equal(6000m, profile.MonthlySalary);
        Assert.Equal("9999999", profile.EpfNumber);
    }

    // ─── Money is reported, never shrugged off ──────────────────────────

    // A mistyped salary silently ignored is someone paid the wrong amount
    // with nothing on screen to say why.
    [Fact]
    public async Task ABadAmountFailsItsRowWithTheRowNumber()
    {
        var result = await Import(
            "Employee Email,Monthly Salary",
            "aisyah@x.com,not-a-number");

        Assert.Equal(0, result.Imported);
        var error = Assert.Single(result.Errors);
        Assert.Equal(2, error.Row);
        Assert.Contains("not a valid amount", error.Message);

        // And the stored salary is untouched.
        Assert.Equal(4000m, Profile().MonthlySalary);
    }

    [Fact]
    public async Task ANegativeAmountIsRefused()
    {
        var result = await Import(
            "Employee Email,Monthly Salary",
            "aisyah@x.com,-500");

        Assert.Contains("cannot be negative", Assert.Single(result.Errors).Message);
    }

    [Fact]
    public async Task AmountsAreReadTolerantly()
    {
        await Import(
            "Employee Email,Monthly Salary",
            "aisyah@x.com,\"RM 5,500.00\"");

        Assert.Equal(5500m, Profile().MonthlySalary);
    }

    // ─── Who a row is about ─────────────────────────────────────────────

    [Fact]
    public async Task AnEmployeeIsMatchedByName()
    {
        await Import(
            "Employee Name,Monthly Salary",
            "Aisyah Binti Rahman,5500");

        Assert.Equal(5500m, Profile().MonthlySalary);
    }

    // Creating an account carries a login and a role, and neither belongs in
    // a payroll sheet.
    [Fact]
    public async Task ARowNamingNobodyIsReported()
    {
        var result = await Import(
            "Employee Email,Monthly Salary",
            "nobody@x.com,5500");

        Assert.Equal(0, result.Imported);
        Assert.Contains("No employee", Assert.Single(result.Errors).Message);
    }

    // An org member with no payroll profile yet gets one — filling those in
    // for a roster that has none is the whole point.
    [Fact]
    public async Task AMemberWithNoProfileYetGetsOne()
    {
        var result = await Import(
            "Employee Email,Monthly Salary",
            "bala@x.com,3000");

        Assert.Equal(1, result.Imported);
        Assert.Equal(3000m, _db.EmployeeProfiles.AsNoTracking()
            .Single(p => p.UserId == "usr-2").MonthlySalary);
    }

    [Fact]
    public async Task OneBadRowDoesNotStopTheOthers()
    {
        var result = await Import(
            "Employee Email,Monthly Salary",
            "aisyah@x.com,5500",
            "nobody@x.com,1000",
            "bala@x.com,3000");

        Assert.Equal(2, result.Imported);
        Assert.Single(result.Errors);
    }

    // ─── The file contract ──────────────────────────────────────────────

    [Fact]
    public async Task AFileWithNoIdentityColumnIsRefused()
    {
        var result = await Import("Monthly Salary", "5500");

        Assert.NotEmpty(result.Errors);
        Assert.Contains("Missing required column", result.Errors[0].Message);
    }

    [Fact]
    public async Task AHeaderWithNoRowsIsRefused()
    {
        var result = await Import("Employee Email,Monthly Salary");

        Assert.Contains("no data rows", Assert.Single(result.Errors).Message);
    }

    // A template downloaded and uploaded untouched must import nobody
    // rather than creating a fictional employee.
    [Fact]
    public async Task TheUntouchedTemplateImportsNobody()
    {
        var template = _service.BuildTemplate(TabularFormat.Csv);

        var result = await _service.ImportAsync(template.Content, TabularFormat.Csv);

        Assert.Equal(0, result.Imported);
        Assert.Equal(1, result.Skipped);
        Assert.Empty(result.Errors);
    }

    // ─── Readiness fields the sheet gained ──────────────────────────────

    // Both of these gate payroll readiness, and until they were columns the
    // round-trip could not finish a married employee or one on a SOCSO scheme.
    [Fact]
    public async Task ImportsSpouseWorkingAndSocsoScheme()
    {
        await Import(
            "Employee Email,Spouse Working,SOCSO Scheme",
            "aisyah@x.com,Yes,EMPLOYMENT_INJURY_ONLY");

        Assert.True(Profile().SpouseWorking);
        Assert.Equal(SocsoScheme.EMPLOYMENT_INJURY_ONLY, Profile().SocsoScheme);
    }

    // The subtle one. SpouseWorking is tri-state and readiness tests it for
    // NULL, so a blank cell becoming "No" would mark a married employee
    // complete when nobody has answered the question.
    [Fact]
    public async Task ABlankSpouseWorkingStaysNullRatherThanBecomingNo()
    {
        await Import(
            "Employee Email,Spouse Working,Marital Status",
            "aisyah@x.com,,MARRIED");

        Assert.Null(Profile().SpouseWorking);
    }

    [Fact]
    public async Task ABlankSpouseWorkingDoesNotClearAnAnswerAlreadyOnFile()
    {
        await Import("Employee Email,Spouse Working", "aisyah@x.com,No");
        Assert.False(Profile().SpouseWorking);

        await Import("Employee Email,Department", "aisyah@x.com,Operations");

        Assert.False(Profile().SpouseWorking);
    }

    // ─── The export covers everyone ─────────────────────────────────────

    // A member with no EmployeeProfile row is exactly who this sheet is for,
    // and exporting only existing profiles left them out — so the roster listed
    // them as unready with no bulk way to fix it.
    [Fact]
    public async Task TheExportIncludesMembersWithNoProfileYet()
    {
        var withoutProfile = new OrganizationMembership
        {
            OrganizationId = "org-1",
            UserId = "usr-new",
            Role = "Employee",
            EmployeeNumber = "EMP-777",
        };
        _db.Users.Add(new User { Id = "usr-new", Email = "newbie@x.com", Name = "New Bie" });
        _db.OrganizationMemberships.Add(withoutProfile);
        await _db.SaveChangesAsync();

        var text = Encoding.UTF8.GetString(
            (await _service.ExportAsync(TabularFormat.Csv)).Content);

        Assert.Contains("newbie@x.com", text);
        Assert.Contains("New Bie", text);
    }

    // ─── The export round-trips ─────────────────────────────────────────

    // Export, edit, import back. If the two shapes drifted, an admin's first
    // import would fail on a file this system produced.
    [Fact]
    public async Task TheExportImportsBackCleanly()
    {
        await Import(
            "Employee Email,Monthly Salary,EPF No,Salary Type,Join Date",
            "aisyah@x.com,5500,7654321,MONTHLY,2024-03-01");

        var export = await _service.ExportAsync(TabularFormat.Csv);
        var result = await _service.ImportAsync(export.Content, TabularFormat.Csv);

        Assert.Empty(result.Errors);

        var profile = Profile();
        Assert.Equal(5500m, profile.MonthlySalary);
        Assert.Equal("7654321", profile.EpfNumber);
        Assert.Equal(new DateTime(2024, 3, 1), profile.JoinDate);
    }

    [Fact]
    public async Task ImportingIsAudited()
    {
        await Import("Employee Email,Monthly Salary", "aisyah@x.com,5500");

        Assert.True(_audit.Recorded("payroll.employee-import"));
    }
}
