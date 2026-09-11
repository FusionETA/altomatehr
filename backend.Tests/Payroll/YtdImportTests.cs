using System.Text;
using AltomateHR.Api.Common.Tabular;
using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Tests.Audit;
using AltomateHR.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Tests.Payroll;

// Reading a year of history out of a spreadsheet.
//
// The one thing worth stating up front: an imported month's figures are
// taken EXACTLY as typed. They were paid — what this engine would have
// calculated is beside the point.
public class YtdImportParserTests
{
    private const string Header =
        "Employee Name,Personal ID,Basic Salary,PCB,CP38,Zakat,Employee EPF,Employer EPF,"
        + "Employee SOCSO,Employer SOCSO,Employee EIS,Employer EIS,HRDF,Bonus,Commission,"
        + "Overtime,Other Allowance";

    private static byte[] Csv(params string[] lines) =>
        Encoding.UTF8.GetBytes(string.Join("\n", lines));

    private static YtdImportParser.Result Parse(params string[] lines) =>
        YtdImportParser.Parse(Csv([Header, .. lines]), TabularFormat.Csv);

    private static string Month(
        string name, string basic = "5000", string pcb = "110", string epf = "550",
        string bonus = "0") =>
        $"{name},,{basic},{pcb},0,0,{epf},650,24.75,86.65,9.90,9.90,0,{bonus},0,0,0";

    private const string NameRow = "Aisyah Binti Rahman,,,,,,,,,,,,,,,,";

    // ─── The shape ──────────────────────────────────────────────────────

    [Fact]
    public void AnEmployeeBlockIsANameFollowedByMonths()
    {
        var result = Parse(
            "Aisyah Binti Rahman,900101-14-5567,,,,,,,,,,,,,,,",
            Month("January"),
            Month("February"));

        Assert.True(result.Ok, string.Join("; ", result.Errors));

        var employee = Assert.Single(result.Employees);
        Assert.Equal("Aisyah Binti Rahman", employee.EmployeeName);
        Assert.Equal("900101-14-5567", employee.PersonalId);
        Assert.Equal([1, 2], employee.Months.Select(m => m.Month));
    }

    [Fact]
    public void SeveralEmployeesEachGetTheirOwnBlock()
    {
        var result = Parse(
            NameRow,
            Month("January"),
            "Tan Wei Ming,,,,,,,,,,,,,,,,",
            Month("January"),
            Month("February"));

        Assert.Equal(2, result.Employees.Count);
        Assert.Single(result.Employees[0].Months);
        Assert.Equal(2, result.Employees[1].Months.Count);
    }

    // A month before the employee joined is absent, not a zero payslip
    // nobody was given.
    [Fact]
    public void AnEmptyMonthIsSkipped()
    {
        var result = Parse(
            NameRow,
            "January,,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0",
            Month("February"));

        Assert.Equal([2], Assert.Single(result.Employees).Months.Select(m => m.Month));
    }

    [Theory]
    [InlineData("January", 1)]
    [InlineData("Jan", 1)]
    [InlineData("december", 12)]
    [InlineData("Dec", 12)]
    [InlineData("7", 7)]
    public void MonthsAreReadInSeveralSpellings(string label, int expected)
    {
        var result = Parse(NameRow, Month(label));

        Assert.Equal(expected, Assert.Single(Assert.Single(result.Employees).Months).Month);
    }

    // ─── The figures ────────────────────────────────────────────────────

    [Fact]
    public void TheAmountsAreTakenAsTyped()
    {
        var month = Assert.Single(Assert.Single(
            Parse(NameRow, Month("January")).Employees).Months);

        Assert.Equal(5000m, month.BasicSalary);
        Assert.Equal(110m, month.Pcb);
        Assert.Equal(550m, month.EpfEmployee);
        Assert.Equal(650m, month.EpfEmployer);
        Assert.Equal(24.75m, month.SocsoEmployee);
    }

    // Both derived, so an imported month reconciles the way a computed one
    // does.
    [Fact]
    public void GrossAndNetAreDerivedFromTheColumns()
    {
        var month = Assert.Single(Assert.Single(
            Parse(NameRow, Month("January", bonus: "2000")).Employees).Months);

        Assert.Equal(7000m, month.Gross);
        Assert.Equal(7000m - (550m + 24.75m + 9.90m + 110m), month.Net);
    }

    // An optional column routes through its category, so an imported bonus
    // feeds next month's additional-remuneration base like a computed one.
    [Fact]
    public void AnOptionalColumnBecomesItsCategory()
    {
        var month = Assert.Single(Assert.Single(
            Parse(NameRow, Month("January", bonus: "2000")).Employees).Months);

        Assert.Equal(2000m, month.CategoryAmounts[PayrollAdjustmentCategories.WagesBonusAnnual]);
    }

    // The sheet came from another system: "RM 5,000.00" is a number an admin
    // typed in good faith.
    // Quoted, because an unquoted comma is a cell break — the thousands
    // separator only survives if the sheet quotes the field, which every
    // spreadsheet exporter does.
    [Theory]
    [InlineData("\"RM 5,000.00\"", 5000)]
    [InlineData("\"5,000\"", 5000)]
    [InlineData(" 5000 ", 5000)]
    [InlineData("5000.50", 5000.50)]
    public void AmountsAreReadTolerantly(string raw, decimal expected)
    {
        var result = Parse(
            NameRow,
            $"January,,{raw},110,0,0,550,650,24.75,86.65,9.90,9.90,0,0,0,0,0");

        Assert.Equal(expected, Assert.Single(Assert.Single(result.Employees).Months).BasicSalary);
    }

    // ─── Refusals and warnings ──────────────────────────────────────────

    [Fact]
    public void AMissingMandatoryColumnIsRefused()
    {
        var result = YtdImportParser.Parse(
            Csv("Employee Name,Personal ID,Basic Salary", "Aisyah,,5000"), TabularFormat.Csv);

        Assert.False(result.Ok);
        Assert.Contains("required columns are missing", result.Errors[0]);
        Assert.Contains("pcb", result.Errors[0]);
    }

    [Fact]
    public void AMissingHeaderRowIsRefused()
    {
        var result = YtdImportParser.Parse(Csv("some,other,sheet", "1,2,3"), TabularFormat.Csv);

        Assert.False(result.Ok);
        Assert.Contains("No header row", result.Errors[0]);
    }

    // A mistyped column silently dropping a year of bonuses is exactly the
    // failure this import must not have.
    [Fact]
    public void AnUnrecognisedColumnIsReported()
    {
        var result = YtdImportParser.Parse(
            Csv(Header + ",Bonis", NameRow + ",", Month("January") + ",500"),
            TabularFormat.Csv);

        Assert.True(result.Ok, string.Join("; ", result.Errors));
        Assert.Contains("Bonis", result.UnrecognisedColumns);
    }

    [Fact]
    public void ANonNumericAmountIsReportedWithItsRow()
    {
        var result = Parse(
            NameRow,
            "January,,not-a-number,110,0,0,550,650,24.75,86.65,9.90,9.90,0,0,0,0,0");

        Assert.False(result.Ok);
        Assert.Contains("not-a-number", result.Errors[0]);
    }

    [Fact]
    public void AMonthRowBeforeAnyNameIsReported()
    {
        var result = Parse(Month("January"));

        Assert.False(result.Ok);
        Assert.Contains("before any employee name", result.Errors[0]);
    }

    // Column ORDER does not matter — the header text is the contract.
    [Fact]
    public void ColumnsMayBeInAnyOrder()
    {
        var result = YtdImportParser.Parse(
            Csv("PCB,Employee Name,Basic Salary,Personal ID,Employee EPF,Employer EPF,"
                + "Employee SOCSO,Employer SOCSO,Employee EIS,Employer EIS",
                ",Aisyah,,,,,,,,",
                "110,January,5000,,550,650,24.75,86.65,9.90,9.90"),
            TabularFormat.Csv);

        Assert.True(result.Ok, string.Join("; ", result.Errors));

        var month = Assert.Single(Assert.Single(result.Employees).Months);
        Assert.Equal(5000m, month.BasicSalary);
        Assert.Equal(110m, month.Pcb);
    }

    [Theory]
    [InlineData("EMPLOYEE EPF")]
    [InlineData("employee  epf")]
    public void HeadersMatchCaseAndSpacingInsensitively(string variant)
    {
        var result = YtdImportParser.Parse(
            Csv(Header.Replace("Employee EPF", variant), NameRow, Month("January")),
            TabularFormat.Csv);

        Assert.True(result.Ok, string.Join("; ", result.Errors));
        Assert.Equal(550m, Assert.Single(Assert.Single(result.Employees).Months).EpfEmployee);
    }
}

// The import itself: matching names to people, and writing the months as
// SUBMITTED runs that later PCB reads from.
public class YtdImportServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly StubCurrentUser _currentUser = new();
    private readonly FakeAuditService _audit = new();
    private readonly YtdImportService _service;

    public YtdImportServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"ytd-import-{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options, _currentUser);

        _service = new YtdImportService(
            new PayrollRunRepository(_db),
            new PayslipRepository(_db),
            TestDirectory.Over(
                new OrganizationMembershipRepository(_db),
                new UserRepository(_db),
                new EmployeeProfileRepository(_db)),
            _currentUser,
            _audit);

        _db.Users.Add(new User { Id = "usr-1", Email = "aisyah@x.com", Name = "Aisyah Binti Rahman" });
        _db.EmployeeProfiles.Add(new EmployeeProfile
        {
            Id = "emp-1", OrganizationId = "org-1", UserId = "usr-1",
            IdNumber = "900101-14-5567",
        });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private const string Header =
        "Employee Name,Personal ID,Basic Salary,PCB,CP38,Zakat,Employee EPF,Employer EPF,"
        + "Employee SOCSO,Employer SOCSO,Employee EIS,Employer EIS,HRDF,Bonus,Commission,"
        + "Overtime,Other Allowance";

    private static byte[] Sheet(string name, string? personalId, params int[] months)
    {
        var lines = new List<string> { Header, $"{name},{personalId},,,,,,,,,,,,,,," };

        foreach (var m in months)
        {
            lines.Add($"{m},,5000,110,0,0,550,650,24.75,86.65,9.90,9.90,0,0,0,0,0");
        }

        return Encoding.UTF8.GetBytes(string.Join("\n", lines));
    }

    // ─── Importing ──────────────────────────────────────────────────────

    [Fact]
    public async Task ImportedMonthsBecomeSubmittedRuns()
    {
        var result = await _service.ImportAsync(
            2026, Sheet("Aisyah Binti Rahman", null, 1, 2, 3), TabularFormat.Csv);

        Assert.True(result.Ok, string.Join("; ", result.Errors));
        Assert.Equal(3, result.MonthsImported);
        Assert.Equal(3, result.PayslipsImported);

        var runs = await _db.PayrollRuns.ToListAsync();
        Assert.Equal(3, runs.Count);
        Assert.All(runs, r =>
        {
            // SUBMITTED is the only status year-to-date reads; IMPORTED marks
            // these as typed rather than computed.
            Assert.Equal(PayrollRunStatus.SUBMITTED, r.Status);
            Assert.Equal(PayrollRunSource.IMPORTED, r.Source);
        });
    }

    // The whole reason the import exists: a mid-year migration has to leave
    // PCB a year-to-date to annualise against.
    [Fact]
    public async Task ImportedMonthsCountTowardsYearToDate()
    {
        await _service.ImportAsync(
            2026, Sheet("Aisyah Binti Rahman", null, 1, 2), TabularFormat.Csv);

        var ytd = await new PayslipRepository(_db).GetYtdByEmployeeAsync(2026, excludeRunId: null);

        Assert.True(ytd.ContainsKey("emp-1"));
        Assert.Equal(220m, ytd["emp-1"].Pcb);
    }

    [Fact]
    public async Task TheFiguresAreStoredAsTyped()
    {
        await _service.ImportAsync(2026, Sheet("Aisyah Binti Rahman", null, 1), TabularFormat.Csv);

        var payslip = await _db.Payslips.SingleAsync();

        Assert.Equal(5000m, payslip.GrossPay);
        Assert.Equal(110m, payslip.Pcb);
        Assert.Equal(550m, payslip.EpfEmployee);
        // No worksheet: the PCB was decided elsewhere, and inventing one
        // would claim a derivation this engine never performed.
        Assert.Null(payslip.PcbCalculationJson);
    }

    [Fact]
    public async Task TheRunTotalsAreRolledUp()
    {
        await _service.ImportAsync(2026, Sheet("Aisyah Binti Rahman", null, 1), TabularFormat.Csv);

        var run = await _db.PayrollRuns.SingleAsync();

        Assert.Equal(1, run.EmployeeCount);
        Assert.Equal(5000m, run.TotalGross);
        Assert.Equal(110m, run.TotalPcb);
    }

    // ─── Matching ───────────────────────────────────────────────────────

    [Fact]
    public async Task AnEmployeeIsMatchedByIcEvenWhenTheNameDiffers()
    {
        var result = await _service.ImportAsync(
            2026, Sheet("AISYAH BT RAHMAN", "900101145567", 1), TabularFormat.Csv);

        Assert.Equal(1, result.PayslipsImported);
        Assert.Empty(result.UnmatchedNames);
    }

    [Fact]
    public async Task AnEmployeeIsMatchedByNameWhenNoIcIsGiven()
    {
        var result = await _service.ImportAsync(
            2026, Sheet("aisyah binti rahman", null, 1), TabularFormat.Csv);

        Assert.Equal(1, result.PayslipsImported);
    }

    [Fact]
    public async Task AnUnmatchedNameIsReported()
    {
        var result = await _service.ImportAsync(
            2026, Sheet("Nobody At All", null, 1), TabularFormat.Csv);

        Assert.False(result.Ok);
        Assert.Contains("matched an employee", result.Errors[0]);
    }

    // ─── Not overwriting real payroll ───────────────────────────────────

    // Replacing a COMPUTED run with typed figures would destroy the payslips
    // those figures were reconciled against.
    [Fact]
    public async Task AComputedMonthIsNeverOverwritten()
    {
        _db.PayrollRuns.Add(new PayrollRun
        {
            Id = "run-real", OrganizationId = "org-1",
            PeriodYear = 2026, PeriodMonth = 1,
            Status = PayrollRunStatus.SUBMITTED, Source = PayrollRunSource.COMPUTED,
        });
        await _db.SaveChangesAsync();

        var result = await _service.ImportAsync(
            2026, Sheet("Aisyah Binti Rahman", null, 1, 2), TabularFormat.Csv);

        Assert.Equal(1, result.MonthsImported);
        Assert.Contains("January 2026", result.SkippedMonths);

        var january = await _db.PayrollRuns.SingleAsync(r => r.PeriodMonth == 1);
        Assert.Equal(PayrollRunSource.COMPUTED, january.Source);
    }

    // A corrected sheet must not double the history.
    [Fact]
    public async Task ReimportingReplacesRatherThanDuplicates()
    {
        await _service.ImportAsync(2026, Sheet("Aisyah Binti Rahman", null, 1), TabularFormat.Csv);
        await _service.ImportAsync(2026, Sheet("Aisyah Binti Rahman", null, 1), TabularFormat.Csv);

        Assert.Single(await _db.PayrollRuns.ToListAsync());
        Assert.Single(await _db.Payslips.ToListAsync());
    }

    // ─── Preview writes nothing ─────────────────────────────────────────

    [Fact]
    public async Task PreviewReportsWithoutWriting()
    {
        var preview = await _service.PreviewAsync(
            2026, Sheet("Aisyah Binti Rahman", null, 1, 2), TabularFormat.Csv);

        Assert.True(preview.Ok);
        var row = Assert.Single(preview.Employees);
        Assert.Equal([1, 2], row.Months);
        Assert.Equal(10000m, row.TotalGross);

        Assert.Empty(await _db.PayrollRuns.ToListAsync());
    }

    [Fact]
    public async Task PreviewWarnsAboutMonthsItWillSkip()
    {
        _db.PayrollRuns.Add(new PayrollRun
        {
            Id = "run-real", OrganizationId = "org-1",
            PeriodYear = 2026, PeriodMonth = 1,
            Status = PayrollRunStatus.SUBMITTED, Source = PayrollRunSource.COMPUTED,
        });
        await _db.SaveChangesAsync();

        var preview = await _service.PreviewAsync(
            2026, Sheet("Aisyah Binti Rahman", null, 1), TabularFormat.Csv);

        Assert.Contains(preview.Warnings, w => w.Contains("January 2026"));
    }

    // ─── The template round-trips ───────────────────────────────────────

    // The file the system hands out must be a file it can read back. If they
    // drift, the admin's first import fails on a sheet we generated.
    [Fact]
    public async Task TheTemplateParsesBackCleanly()
    {
        var template = await _service.BuildTemplateAsync(2026, TabularFormat.Csv);

        var parsed = YtdImportParser.Parse(template.Content, TabularFormat.Csv);

        Assert.True(parsed.Ok, string.Join("; ", parsed.Errors));
        Assert.Empty(parsed.UnrecognisedColumns);

        // The roster is pre-filled, with no figures yet.
        var employee = Assert.Single(parsed.Employees);
        Assert.Equal("Aisyah Binti Rahman", employee.EmployeeName);
        Assert.Empty(employee.Months);
    }

    [Fact]
    public async Task ImportingIsAudited()
    {
        await _service.ImportAsync(2026, Sheet("Aisyah Binti Rahman", null, 1), TabularFormat.Csv);

        Assert.True(_audit.Recorded("payroll.ytd-import"));
    }
}
