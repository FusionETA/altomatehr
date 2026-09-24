using AltomateHR.Api.Common.Tabular;
using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Modules.Policies;
using AltomateHR.Api.Modules.Policies.Entities;
using AltomateHR.Api.Tests.Audit;
using AltomateHR.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Tests.Payroll;

// Bulk-importing a run's manual adjustments.
//
// The two things worth pinning are the ones that cost money if they are wrong:
// manual lines REPLACE (so an omitted line is a deleted line), and the salary
// columns do NOT (so a blank cell leaves a salary alone). Everything else here
// exists to keep a bad file from half-applying.
public class PayrollAdjustmentImportTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly StubCurrentUser _currentUser = new();
    private readonly FakeAuditService _audit = new();
    private readonly PayrollAdjustmentImportService _import;
    private readonly PayrollRunAdjustmentRepository _adjustments;
    private readonly EmployeeProfileRepository _profiles;

    public PayrollAdjustmentImportTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"payroll-adjustment-import-{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options, _currentUser);

        _profiles = new EmployeeProfileRepository(_db);
        _adjustments = new PayrollRunAdjustmentRepository(_db);
        var runs = new PayrollRunRepository(_db);
        var directory = TestDirectory.Over(
            new OrganizationMembershipRepository(_db),
            new UserRepository(_db),
            _profiles);

        var policies = new PolicyService(
            new EmployeePolicyRepository(_db),
            new PolicyLeaveEntitlementRepository(_db),
            directory);

        var adjustmentService = new PayrollRunAdjustmentService(
            _adjustments,
            runs,
            directory,
            policies,
            new StubPayrollHours(),
            new EmployeeLoanService(new EmployeeLoanRepository(_db), runs, directory, _audit),
            _audit,
            new StubApprovedOvertime());

        _import = new PayrollAdjustmentImportService(
            runs,
            new PayrollRunMemberRepository(_db),
            adjustmentService,
            _adjustments,
            _profiles,
            directory,
            new SalaryChangeService(
                new SalaryChangeRepository(_db),
                runs,
                new PayslipRepository(_db),
                _adjustments,
                new PayrollSettingsService(new PayrollSettingsRepository(_db), _audit),
                directory,
                _currentUser,
                _audit));
    }

    public void Dispose() => _db.Dispose();

    // ─── Fixtures ───────────────────────────────────────────────────────

    private async Task<PayrollRun> AddRunAsync(PayrollRunStatus status = PayrollRunStatus.DRAFT)
    {
        var run = new PayrollRun
        {
            OrganizationId = "org-1",
            PeriodYear = 2026,
            PeriodMonth = 3,
            Status = status,
        };
        _db.PayrollRuns.Add(run);
        await _db.SaveChangesAsync();
        return run;
    }

    // A profile complete enough to pass PayrollProfileReadiness, so the
    // importer counts them as payable.
    private async Task<EmployeeProfile> AddEmployeeAsync(
        string name, decimal salary = 5000m, SalaryType type = SalaryType.MONTHLY)
    {
        var userId = $"usr-{name.Replace(" ", "-").ToLowerInvariant()}";

        _db.Users.Add(new User { Id = userId, Email = $"{userId}@example.com", Name = name });
        _db.OrganizationMemberships.Add(new OrganizationMembership
        {
            OrganizationId = "org-1",
            UserId = userId,
            Role = "Employee",
            EmployeeNumber = "EMP-1",
        });

        var profile = new EmployeeProfile
        {
            OrganizationId = "org-1",
            UserId = userId,
            Gender = Gender.FEMALE,
            DateOfBirth = new DateTime(1990, 1, 1),
            Nationality = "Malaysian",
            IdType = IdType.NRIC,
            IdNumber = "900101015523",
            MaritalStatus = MaritalStatus.SINGLE,
            SalaryType = type,
            MonthlySalary = type == SalaryType.MONTHLY ? salary : null,
            HourlyRate = type == SalaryType.HOURLY ? 25m : null,
            JoinDate = new DateTime(2020, 1, 1),
            ContributeToEpf = false,
        };
        _db.EmployeeProfiles.Add(profile);
        await _db.SaveChangesAsync();
        return profile;
    }

    // A CSV in the template's column order. Written by hand rather than round-
    // tripped through the template so a silent column reorder fails a test.
    private static byte[] Csv(params string[] dataRows)
    {
        var header = string.Join(",", PayrollAdjustmentImportSheet.Columns.Select(
            c => c.Required ? $"*{c.Label}" : c.Label));
        return System.Text.Encoding.UTF8.GetBytes(
            header + "\r\n" + string.Join("\r\n", dataRows) + "\r\n");
    }

    private static string Row(
        string name, string category = "", string label = "", string amount = "",
        string recurring = "", string currentSalary = "", string newSalary = "",
        string reason = "", string effective = "", string notes = "") =>
        string.Join(",", name, category, label, amount, recurring, currentSalary,
            newSalary, reason, effective, notes);

    private Task<PayrollAdjustmentImportResult> ImportAsync(PayrollRun run, byte[] csv) =>
        _import.ImportAsync(run.Id, csv, TabularFormat.Csv);

    // ─── Manual lines ───────────────────────────────────────────────────

    [Fact]
    public async Task ImportsAManualLineAgainstTheNamedEmployee()
    {
        var run = await AddRunAsync();
        var profile = await AddEmployeeAsync("Aisyah Binti Rahman");

        var result = await ImportAsync(run, Csv(
            Row("Aisyah Binti Rahman", PayrollAdjustmentCategories.AllowanceTravelOfficial, "Site travel", "250.00")));

        Assert.True(result.Ok, result.Message);
        Assert.Equal(1, result.EmployeesAffected);
        Assert.Equal(1, result.LinesWritten);

        var saved = await _adjustments.GetAsync(run.Id, profile.Id);
        var lines = PayrollRunAdjustments.ParseManualLineItems(saved!.ManualLineItemsJson);
        Assert.Equal(PayrollAdjustmentCategories.AllowanceTravelOfficial, Assert.Single(lines).Category);
        Assert.Equal(250.00m, lines[0].Amount);
    }

    [Fact]
    public async Task SeveralRowsForOnePersonBecomeSeveralLines()
    {
        var run = await AddRunAsync();
        var profile = await AddEmployeeAsync("Aisyah Binti Rahman");

        var result = await ImportAsync(run, Csv(
            Row("Aisyah Binti Rahman", PayrollAdjustmentCategories.AllowanceTravelOfficial, "Travel", "100"),
            Row("Aisyah Binti Rahman", PayrollAdjustmentCategories.AllowanceMeal, "Meals", "50")));

        Assert.True(result.Ok, result.Message);
        Assert.Equal(2, result.LinesWritten);

        var saved = await _adjustments.GetAsync(run.Id, profile.Id);
        Assert.Equal(2, PayrollRunAdjustments.ParseManualLineItems(saved!.ManualLineItemsJson).Count);
    }

    // The dangerous half. A line left out of the file is a line deleted, which
    // is why the template ships pre-filled with what is already there.
    [Fact]
    public async Task ImportReplacesExistingManualLinesRatherThanAppending()
    {
        var run = await AddRunAsync();
        var profile = await AddEmployeeAsync("Aisyah Binti Rahman");

        await ImportAsync(run, Csv(Row("Aisyah Binti Rahman", PayrollAdjustmentCategories.AllowanceTravelOfficial, "Old", "100")));
        await ImportAsync(run, Csv(Row("Aisyah Binti Rahman", PayrollAdjustmentCategories.AllowanceMeal, "New", "75")));

        var saved = await _adjustments.GetAsync(run.Id, profile.Id);
        var lines = PayrollRunAdjustments.ParseManualLineItems(saved!.ManualLineItemsJson);
        Assert.Equal(PayrollAdjustmentCategories.AllowanceMeal, Assert.Single(lines).Category);
    }

    // Same rule, seen from the other side: someone the file says nothing about
    // loses the lines they had, and the result says how many that was.
    [Fact]
    public async Task AnEmployeeMissingFromTheFileHasTheirLinesCleared()
    {
        var run = await AddRunAsync();
        var kept = await AddEmployeeAsync("Aisyah Binti Rahman");
        var dropped = await AddEmployeeAsync("Chan Mei Ling");

        await ImportAsync(run, Csv(
            Row("Aisyah Binti Rahman", PayrollAdjustmentCategories.AllowanceTravelOfficial, "Travel", "100"),
            Row("Chan Mei Ling", PayrollAdjustmentCategories.AllowanceMeal, "Meals", "50")));

        var result = await ImportAsync(run, Csv(
            Row("Aisyah Binti Rahman", PayrollAdjustmentCategories.AllowanceTravelOfficial, "Travel", "100")));

        Assert.True(result.Ok, result.Message);
        Assert.Equal(1, result.EmployeesCleared);

        var keptRow = await _adjustments.GetAsync(run.Id, kept.Id);
        Assert.Single(PayrollRunAdjustments.ParseManualLineItems(keptRow!.ManualLineItemsJson));

        var droppedRow = await _adjustments.GetAsync(run.Id, dropped.Id);
        Assert.Empty(PayrollRunAdjustments.ParseManualLineItems(droppedRow!.ManualLineItemsJson));
    }

    // ─── Salary, the opposite semantics ─────────────────────────────────

    [Fact]
    public async Task ABlankSalaryCellLeavesTheSalaryAlone()
    {
        var run = await AddRunAsync();
        var profile = await AddEmployeeAsync("Aisyah Binti Rahman", salary: 5000m);

        var result = await ImportAsync(run, Csv(
            Row("Aisyah Binti Rahman", PayrollAdjustmentCategories.AllowanceTravelOfficial, "Travel", "100")));

        Assert.True(result.Ok, result.Message);
        Assert.Equal(0, result.SalaryChangesApplied);
        Assert.Equal(5000m, (await _profiles.GetByUserAsync(profile.UserId))!.MonthlySalary);
    }

    [Fact]
    public async Task ANewSalaryIsAppliedAndRecordedAsASalaryChange()
    {
        var run = await AddRunAsync();
        var profile = await AddEmployeeAsync("Aisyah Binti Rahman", salary: 5000m);

        var result = await ImportAsync(run, Csv(
            Row("Aisyah Binti Rahman", newSalary: "6000", reason: "PROMOTION", notes: "Team lead")));

        Assert.True(result.Ok, result.Message);
        Assert.Equal(1, result.SalaryChangesApplied);
        Assert.Equal(6000m, (await _profiles.GetByUserAsync(profile.UserId))!.MonthlySalary);

        // The audit trail is the whole point of routing this through
        // SalaryChangeService rather than writing the column straight on.
        var change = Assert.Single(_db.SalaryChanges.ToList());
        Assert.Equal(5000m, change.PreviousMonthlySalary);
        Assert.Equal(6000m, change.NewMonthlySalary);
        Assert.Equal(SalaryChangeReason.PROMOTION, change.Reason);
    }

    // Without a date the change belongs to the month being run, not to today.
    [Fact]
    public async Task ASalaryChangeWithoutADateTakesEffectFromTheRunsPeriod()
    {
        var run = await AddRunAsync();
        await AddEmployeeAsync("Aisyah Binti Rahman", salary: 5000m);

        await ImportAsync(run, Csv(Row("Aisyah Binti Rahman", newSalary: "6000")));

        Assert.Equal(new DateTime(2026, 3, 1), Assert.Single(_db.SalaryChanges.ToList()).EffectiveDate);
    }

    // The column sets a MONTHLY figure, so an hourly employee is reported
    // rather than silently moved onto a different basis.
    [Fact]
    public async Task AnHourlyEmployeeIsSkippedByNameRatherThanRebased()
    {
        var run = await AddRunAsync();
        var profile = await AddEmployeeAsync("Syafiq Bin Osman", type: SalaryType.HOURLY);

        var result = await ImportAsync(run, Csv(Row("Syafiq Bin Osman", newSalary: "6000")));

        Assert.True(result.Ok, result.Message);
        Assert.Equal(0, result.SalaryChangesApplied);
        Assert.Equal("Syafiq Bin Osman", Assert.Single(result.SalarySkippedNotMonthly));
        Assert.Null((await _profiles.GetByUserAsync(profile.UserId))!.MonthlySalary);
    }

    [Fact]
    public async Task TwoDifferentSalariesForOnePersonRejectTheFile()
    {
        var run = await AddRunAsync();
        await AddEmployeeAsync("Aisyah Binti Rahman", salary: 5000m);

        var result = await ImportAsync(run, Csv(
            Row("Aisyah Binti Rahman", PayrollAdjustmentCategories.AllowanceTravelOfficial, "A", "10", newSalary: "6000"),
            Row("Aisyah Binti Rahman", PayrollAdjustmentCategories.AllowanceMeal, "B", "20", newSalary: "7000")));

        Assert.False(result.Ok);
        Assert.Contains("Aisyah Binti Rahman", result.Message);
    }

    // ─── Rejection, all-or-nothing ──────────────────────────────────────

    [Fact]
    public async Task AnUnknownNameRejectsTheWholeFileAndWritesNothing()
    {
        var run = await AddRunAsync();
        var profile = await AddEmployeeAsync("Aisyah Binti Rahman");

        var result = await ImportAsync(run, Csv(
            Row("Aisyah Binti Rahman", PayrollAdjustmentCategories.AllowanceTravelOfficial, "Travel", "100"),
            Row("Nobody At All", PayrollAdjustmentCategories.AllowanceMeal, "Meals", "50")));

        Assert.False(result.Ok);
        Assert.Contains("Nobody At All", Assert.Single(result.Errors).Message);
        Assert.Null(await _adjustments.GetAsync(run.Id, profile.Id));
    }

    [Fact]
    public async Task AnUnknownCategoryRejectsTheWholeFile()
    {
        var run = await AddRunAsync();
        var profile = await AddEmployeeAsync("Aisyah Binti Rahman");

        var result = await ImportAsync(run, Csv(
            Row("Aisyah Binti Rahman", "not_a_category", "Travel", "100")));

        Assert.False(result.Ok);
        Assert.Contains("not_a_category", Assert.Single(result.Errors).Message);
        Assert.Null(await _adjustments.GetAsync(run.Id, profile.Id));
    }

    // A negative deduction would ADD to someone's pay — the category already
    // decides the direction.
    [Fact]
    public async Task ANegativeAmountIsRefusedRatherThanFlipped()
    {
        var run = await AddRunAsync();
        await AddEmployeeAsync("Aisyah Binti Rahman");

        var result = await ImportAsync(run, Csv(
            Row("Aisyah Binti Rahman", PayrollAdjustmentCategories.AllowanceTravelOfficial, "Travel", "-100")));

        Assert.False(result.Ok);
        Assert.Contains("negative", Assert.Single(result.Errors).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ARunThatIsNoLongerADraftIsRefused()
    {
        var run = await AddRunAsync(PayrollRunStatus.SUBMITTED);
        await AddEmployeeAsync("Aisyah Binti Rahman");

        var result = await ImportAsync(run, Csv(
            Row("Aisyah Binti Rahman", PayrollAdjustmentCategories.AllowanceTravelOfficial, "Travel", "100")));

        Assert.False(result.Ok);
        Assert.Contains("draft", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnUnknownRunIsNotFoundRatherThanAFileError()
    {
        var result = await _import.ImportAsync("no-such-run", Csv(Row("Anyone")), TabularFormat.Csv);

        Assert.False(result.Ok);
        Assert.False(result.Found);
    }

    // ─── Template ───────────────────────────────────────────────────────

    // Pre-filling is what makes replace semantics survivable: the admin edits
    // what is there instead of remembering it. Read back through TabularReader
    // rather than asserting on bytes — it is the same path the import uses, so
    // a template this cannot parse fails here too.
    [Fact]
    public async Task TheTemplateCarriesExistingLinesAndTheCurrentSalary()
    {
        var run = await AddRunAsync();
        await AddEmployeeAsync("Aisyah Binti Rahman", salary: 5000m);
        await ImportAsync(run, Csv(
            Row("Aisyah Binti Rahman", PayrollAdjustmentCategories.AllowanceTravelOfficial, "Site travel", "250")));

        var rows = await TemplateRowsAsync(run);
        var row = Assert.Single(rows);

        Assert.Equal("Aisyah Binti Rahman", Cell(row, PayrollAdjustmentImportSheet.NameKey));
        Assert.Equal(PayrollAdjustmentCategories.AllowanceTravelOfficial, Cell(row, PayrollAdjustmentImportSheet.CategoryKey));
        Assert.Equal("Site travel", Cell(row, PayrollAdjustmentImportSheet.LabelKey));
        Assert.Equal("250.00", Cell(row, PayrollAdjustmentImportSheet.AmountKey));
        Assert.Equal("5000.00", Cell(row, PayrollAdjustmentImportSheet.CurrentSalaryKey));
    }

    // The template must NOT pre-fill the new-salary column: doing so would make
    // every upload look like a company-wide salary change.
    [Fact]
    public async Task TheTemplateLeavesTheSalaryChangeColumnsEmpty()
    {
        var run = await AddRunAsync();
        await AddEmployeeAsync("Aisyah Binti Rahman", salary: 5000m);

        var row = Assert.Single(await TemplateRowsAsync(run));

        Assert.Equal(string.Empty, Cell(row, PayrollAdjustmentImportSheet.NewSalaryKey));
        Assert.Equal(string.Empty, Cell(row, PayrollAdjustmentImportSheet.ReasonKey));
        Assert.Equal(string.Empty, Cell(row, PayrollAdjustmentImportSheet.EffectiveKey));
    }

    // A template that round-trips: download it untouched, upload it back, and
    // the run must be exactly as it was. If this breaks, replace semantics are
    // deleting lines the admin never touched.
    [Fact]
    public async Task ReUploadingTheUntouchedTemplateChangesNothing()
    {
        var run = await AddRunAsync();
        var profile = await AddEmployeeAsync("Aisyah Binti Rahman", salary: 5000m);
        await ImportAsync(run, Csv(
            Row("Aisyah Binti Rahman", PayrollAdjustmentCategories.AllowanceTravelOfficial, "Site travel", "250")));

        var template = await _import.BuildTemplateAsync(run.Id);
        var result = await _import.ImportAsync(run.Id, template!.Content, TabularFormat.Xlsx);

        Assert.True(result.Ok, result.Message);
        Assert.Equal(0, result.SalaryChangesApplied);
        Assert.Equal(0, result.EmployeesCleared);
        Assert.Equal(5000m, (await _profiles.GetByUserAsync(profile.UserId))!.MonthlySalary);

        var saved = await _adjustments.GetAsync(run.Id, profile.Id);
        var line = Assert.Single(PayrollRunAdjustments.ParseManualLineItems(saved!.ManualLineItemsJson));
        Assert.Equal(PayrollAdjustmentCategories.AllowanceTravelOfficial, line.Category);
        Assert.Equal(250m, line.Amount);
    }

    // ─── Template helpers ───────────────────────────────────────────────

    private async Task<IReadOnlyList<IReadOnlyList<string>>> TemplateRowsAsync(PayrollRun run)
    {
        var template = await _import.BuildTemplateAsync(run.Id);
        Assert.NotNull(template);

        // Sheet one is the fillable one; the Categories sheet is reference only.
        var rows = TabularReader.Read(template!.Content, TabularFormat.Xlsx);
        return rows.Skip(1).ToList();   // drop the header
    }

    private static string Cell(IReadOnlyList<string> row, string key)
    {
        var index = PayrollAdjustmentImportSheet.Columns
            .Select((c, i) => (c.Key, i))
            .First(x => x.Key == key).i;
        return row[index];
    }
}
